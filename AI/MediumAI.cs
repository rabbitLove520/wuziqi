using System.Collections.ObjectModel;
using WuziqiAi.Models;

namespace WuziqiAi.AI;

public enum Difficulty
{
    Easy,
    Medium,
    Hard
}

/// <summary>
/// 中等难度 AI：棋型模板评分 + 必胜/必防的硬判断。
/// 优先级：自己成五 → 挡对手成五 → 自己做活四/双四/四三/双三 → 挡对手同类威胁 → 启发式评分。
/// </summary>
public class MediumAI
{
    private readonly int _boardSize;

    internal static readonly (int Dr, int Dc)[] Directions =
    {
        (1, 0), (0, 1), (1, 1), (1, -1)
    };

    // 棋型模板：'1' 己方、'0' 空位、'X' 对手或棋盘外（视为阻挡）。
    // 只统计包含落子点（中心）的匹配，避免把远离落子点的旧棋型重复计分。
    private static readonly (string Pattern, long Score, bool LiveThree)[] Shapes =
    {
        ("11111", 10_000_000, false), // 五连
        ("011110", 1_000_000, false), // 活四
        ("11110", 120_000, false),    // 冲四
        ("01111", 120_000, false),    // 冲四
        ("11011", 120_000, false),    // 跳四
        ("10111", 120_000, false),    // 跳四
        ("11101", 120_000, false),    // 跳四
        ("011100", 25_000, true),     // 活三
        ("001110", 25_000, true),     // 活三
        ("010110", 25_000, true),     // 跳活三
        ("011010", 25_000, true),     // 跳活三
        ("11100", 1_500, false),      // 眠三
        ("00111", 1_500, false),      // 眠三
        ("11010", 1_500, false),      // 眠三
        ("01011", 1_500, false),      // 眠三
        ("10110", 1_500, false),      // 眠三
        ("01101", 1_500, false),      // 眠三
        ("10011", 1_500, false),      // 眠三
        ("0110", 400, false),         // 活二
        ("01010", 400, false),        // 跳活二
        ("101", 60, false),           // 跳二
        ("11", 50, false)             // 连二
    };

    internal const int LineHalfWidth = 4;
    internal const int Center = LineHalfWidth;

    private static readonly Random Rng = new();

    public MediumAI(int boardSize)
    {
        _boardSize = boardSize;
    }

    public (int row, int col)? GetBestMove(ObservableCollection<GomokuCell> cells, CellState aiState)
    {
        int size = _boardSize;
        var board = new CellState[size * size];
        for (int i = 0; i < board.Length; i++)
            board[i] = cells[i].State;

        var candidates = GetCandidates(board, size);
        if (candidates.Count == 0) return null;

        CellState opponent = aiState == CellState.Black ? CellState.White : CellState.Black;

        var moves = new List<Move>(candidates.Count);
        foreach (var (r, c) in candidates)
            moves.Add(new Move(r, c, Analyze(board, size, r, c, aiState), Analyze(board, size, r, c, opponent)));

        // 1. 自己能成五，直接赢
        var winning = moves.Where(m => m.Own.MakesFive).ToList();
        if (winning.Count > 0) return PickRandom(winning);

        // 2. 对手在此成五，必须挡
        var blocks = moves.Where(m => m.Defense.MakesFive).ToList();
        if (blocks.Count > 0) return PickBest(blocks, m => m.Own.Score);

        // 3. 自己形成活四/双四/四三/双三，属于强制胜
        var forced = moves.Where(m => IsForcedWin(m.Own)).ToList();
        if (forced.Count > 0) return PickBest(forced, m => m.Own.Score);

        // 4. 对手在此形成强制胜，优先封堵
        var defend = moves.Where(m => IsForcedWin(m.Defense)).ToList();
        if (defend.Count > 0) return PickBest(defend, m => m.Own.Score + m.Defense.Score);

        // 5. 常规棋型评分，进攻略优先
        return PickBest(moves, m => m.Own.Score * 12 / 10 + m.Defense.Score);
    }

    internal static bool IsForcedWin(MoveAnalysis move)
    {
        return move.WinningCells >= 2
               || (move.WinningCells >= 1 && move.LiveThrees >= 1)
               || move.LiveThrees >= 2;
    }

    internal static MoveAnalysis Analyze(CellState[] board, int boardSize, int row, int col, CellState state)
    {
        int index = row * boardSize + col;
        CellState original = board[index];
        board[index] = state;

        bool makesFive = false;
        int liveThrees = 0;
        long score = 0;

        foreach (var (dr, dc) in Directions)
        {
            if (CountLine(board, boardSize, row, col, dr, dc, state) >= 5)
                makesFive = true;

            score += ScoreDirection(board, boardSize, row, col, dr, dc, state, out bool liveThree);
            if (liveThree) liveThrees++;
        }

        int winningCells = makesFive ? 0 : FindWinningCells(board, boardSize, row, col, state).Count;

        board[index] = original;

        return new MoveAnalysis(makesFive, winningCells, liveThrees, score);
    }

    private static long ScoreDirection(CellState[] board, int boardSize, int row, int col, int dr, int dc,
        CellState state, out bool liveThree)
    {
        var line = new char[LineHalfWidth * 2 + 1];
        for (int i = 0; i < line.Length; i++)
        {
            int offset = i - Center;
            int r = row + dr * offset;
            int c = col + dc * offset;

            if (!InBoard(boardSize, r, c))
            {
                line[i] = 'X';
                continue;
            }

            CellState s = board[r * boardSize + c];
            line[i] = s == CellState.Empty ? '0' : s == state ? '1' : 'X';
        }

        long best = 0;
        liveThree = false;

        foreach (var (pattern, value, isLiveThree) in Shapes)
        {
            for (int start = 0; start + pattern.Length <= line.Length; start++)
            {
                // 模板必须覆盖中心（刚落下的子）
                if (start + pattern.Length - 1 < Center || start > Center)
                    continue;

                if (!MatchesAt(line, start, pattern))
                    continue;

                if (value > best) best = value;
                if (isLiveThree) liveThree = true;
            }
        }

        return best;
    }

    /// <summary>
    /// 统计落子后新出现的“下一手可成五”的空点。
    /// 两个及以上就无法同时封堵，等同于强制胜。
    /// </summary>
    internal static HashSet<int> FindWinningCells(CellState[] board, int boardSize, int row, int col, CellState state)
    {
        var seen = new HashSet<int>();

        foreach (var (dr, dc) in Directions)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                for (int distance = 1; distance <= LineHalfWidth; distance++)
                {
                    int r = row + dr * distance * sign;
                    int c = col + dc * distance * sign;
                    if (!InBoard(boardSize, r, c)) break;

                    int index = r * boardSize + c;
                    if (board[index] != CellState.Empty) continue;

                    board[index] = state;

                    bool wins = false;
                    foreach (var (dirR, dirC) in Directions)
                    {
                        if (CountLine(board, boardSize, r, c, dirR, dirC, state) >= 5)
                        {
                            wins = true;
                            break;
                        }
                    }

                    board[index] = CellState.Empty;

                    if (wins) seen.Add(index);
                }
            }
        }

        return seen;
    }

    internal static int CountLine(CellState[] board, int boardSize, int row, int col, int dr, int dc, CellState state)
    {
        int count = 1;

        int r = row + dr;
        int c = col + dc;
        while (InBoard(boardSize, r, c) && board[r * boardSize + c] == state)
        {
            count++;
            r += dr;
            c += dc;
        }

        r = row - dr;
        c = col - dc;
        while (InBoard(boardSize, r, c) && board[r * boardSize + c] == state)
        {
            count++;
            r -= dr;
            c -= dc;
        }

        return count;
    }

    internal static List<(int Row, int Col)> GetCandidates(CellState[] board, int boardSize)
    {
        var candidates = new HashSet<(int, int)>();
        bool hasStone = false;

        for (int r = 0; r < boardSize; r++)
        {
            for (int c = 0; c < boardSize; c++)
            {
                if (board[r * boardSize + c] == CellState.Empty) continue;

                hasStone = true;
                for (int dr = -2; dr <= 2; dr++)
                {
                    for (int dc = -2; dc <= 2; dc++)
                    {
                        int rr = r + dr;
                        int cc = c + dc;
                        if (InBoard(boardSize, rr, cc) && board[rr * boardSize + cc] == CellState.Empty)
                            candidates.Add((rr, cc));
                    }
                }
            }
        }

        if (!hasStone)
            return new List<(int, int)> { (boardSize / 2, boardSize / 2) };

        return candidates.ToList();
    }

    internal static bool InBoard(int boardSize, int row, int col)
        => row >= 0 && row < boardSize && col >= 0 && col < boardSize;

    private static bool MatchesAt(char[] line, int start, string pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (line[start + i] != pattern[i]) return false;
        }

        return true;
    }

    private static (int row, int col) PickRandom(List<Move> moves)
    {
        var pick = moves[Rng.Next(moves.Count)];
        return (pick.Row, pick.Col);
    }

    private static (int row, int col) PickBest(List<Move> moves, Func<Move, long> selector)
    {
        long best = long.MinValue;
        var bestMoves = new List<Move>();

        foreach (var move in moves)
        {
            long score = selector(move);
            if (score > best)
            {
                best = score;
                bestMoves.Clear();
                bestMoves.Add(move);
            }
            else if (score == best)
            {
                bestMoves.Add(move);
            }
        }

        return PickRandom(bestMoves);
    }

    internal readonly record struct MoveAnalysis(bool MakesFive, int WinningCells, int LiveThrees, long Score);

    private readonly record struct Move(int Row, int Col, MoveAnalysis Own, MoveAnalysis Defense);
}
