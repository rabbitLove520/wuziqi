using System.Collections.ObjectModel;
using System.Diagnostics;
using WuziqiAi.Models;

namespace WuziqiAi.AI;

/// <summary>
/// 困难难度 AI：中等 AI 的棋型评分 + VCF 连续冲四搜索 + 4 层 negamax（alpha-beta 剪枝）。
/// 搜索带时间上限，可在后台线程安全调用。
/// </summary>
public class HardAI
{
    private const int MaxDepth = 4;          // 4 层：AI → 对手 → AI → 对手
    private const int RootWidth = 12;        // 根节点保留的候选数
    private const int NodeWidth = 8;         // 深层节点保留的候选数
    private const int VcfMaxDepth = 8;       // VCF 最多连续冲四次数
    private const int VcfMaxNodes = 120;     // VCF 节点上限
    private const long VcfBudgetMs = 80;     // VCF 时间上限
    private const long SearchBudgetMs = 400; // 主搜索时间上限
    private const long WinScore = 1_000_000_000;

    private readonly int _boardSize;
    private readonly Stopwatch _clock = new();

    public HardAI(int boardSize)
    {
        _boardSize = boardSize;
    }

    public (int row, int col)? GetBestMove(ObservableCollection<GomokuCell> cells, CellState aiState)
    {
        var board = new CellState[cells.Count];
        for (int i = 0; i < board.Length; i++)
            board[i] = cells[i].State;

        return GetBestMove(board, aiState);
    }

    public (int row, int col)? GetBestMove(CellState[] board, CellState aiState)
    {
        var local = (CellState[])board.Clone();
        CellState opponent = aiState == CellState.Black ? CellState.White : CellState.Black;

        var candidates = MediumAI.GetCandidates(local, _boardSize);
        if (candidates.Count == 0) return null;

        var rootMoves = AnalyzeAll(local, candidates, aiState, opponent);

        // 1. 自己能成五
        var winning = rootMoves.Where(m => m.Own.MakesFive).ToList();
        if (winning.Count > 0) return PickRandom(winning);

        // 2. 挡对手成五
        var blocks = rootMoves.Where(m => m.Defense.MakesFive).ToList();
        if (blocks.Count > 0) return PickBest(blocks, m => m.Own.Score);

        // 3. 自己已有活四/双四/四三/双三
        var forced = rootMoves.Where(m => MediumAI.IsForcedWin(m.Own)).ToList();
        if (forced.Count > 0) return PickBest(forced, m => m.Own.Score);

        // 4. VCF：连续冲四能不能直接杀
        var vcf = FindVcfWin(local, aiState, opponent);
        if (vcf != null) return vcf;

        // 5. 挡对手的强制胜
        var defend = rootMoves.Where(m => MediumAI.IsForcedWin(m.Defense)).ToList();
        if (defend.Count > 0) return PickBest(defend, m => m.Own.Score + m.Defense.Score);

        // 6. 搜索
        return SearchRoot(local, rootMoves, aiState, opponent);
    }

    private List<Move> AnalyzeAll(CellState[] board, List<(int Row, int Col)> candidates, CellState me, CellState opponent)
    {
        var moves = new List<Move>(candidates.Count);
        foreach (var (r, c) in candidates)
        {
            moves.Add(new Move(
                r,
                c,
                MediumAI.Analyze(board, _boardSize, r, c, me),
                MediumAI.Analyze(board, _boardSize, r, c, opponent)));
        }

        return moves;
    }

    private (int row, int col) SearchRoot(CellState[] board, List<Move> rootMoves, CellState me, CellState opponent)
    {
        var ordered = Order(rootMoves, RootWidth);

        _clock.Restart();
        long best = long.MinValue;
        long alpha = -WinScore;
        var bestMoves = new List<Move>();

        foreach (var move in ordered)
        {
            // 超时后不再开启新的根节点搜索，直接采用已找到的最好结果
            if (bestMoves.Count > 0 && _clock.ElapsedMilliseconds > SearchBudgetMs)
                break;

            int index = move.Row * _boardSize + move.Col;
            board[index] = me;

            long value = -Negamax(board, opponent, me, MaxDepth - 1, 1, -WinScore, -alpha);

            board[index] = CellState.Empty;

            if (value > best)
            {
                best = value;
                alpha = value;
                bestMoves.Clear();
                bestMoves.Add(move);
            }
            else if (value == best)
            {
                bestMoves.Add(move);
            }
        }

        if (bestMoves.Count == 0)
            return PickRandom(ordered);

        return PickRandom(bestMoves);
    }

    private long Negamax(CellState[] board, CellState side, CellState opponent, int depth, int ply, long alpha, long beta)
    {
        if (depth <= 0)
            return StaticEval(board, side, opponent);

        if (_clock.ElapsedMilliseconds > SearchBudgetMs)
            return 0;

        var candidates = MediumAI.GetCandidates(board, _boardSize);
        if (candidates.Count == 0) return 0;

        var moves = AnalyzeAll(board, candidates, side, opponent);

        // 当前行棋方有成五，直接结束
        if (moves.Any(m => m.Own.MakesFive))
            return WinScore - ply;

        var ordered = Order(moves, NodeWidth);

        long best = -WinScore;
        foreach (var move in ordered)
        {
            int index = move.Row * _boardSize + move.Col;
            board[index] = side;

            long value;
            if (move.Own.MakesFive)
            {
                value = WinScore - ply;
            }
            else
            {
                value = -Negamax(board, opponent, side, depth - 1, ply + 1, -beta, -alpha);
            }

            board[index] = CellState.Empty;

            if (value > best) best = value;
            if (best > alpha) alpha = best;
            if (alpha >= beta) break;
        }

        return best;
    }

    /// <summary>
    /// 叶子节点评估：当前行棋方的最佳进攻分 - 对手的最佳进攻分，
    /// 再对“强制胜”棋型加权。
    /// </summary>
    private long StaticEval(CellState[] board, CellState side, CellState opponent)
    {
        var candidates = MediumAI.GetCandidates(board, _boardSize);
        if (candidates.Count == 0) return 0;

        long myBest = 0;
        long oppBest = 0;
        bool myForced = false;
        bool oppForced = false;

        foreach (var (r, c) in candidates)
        {
            var mine = MediumAI.Analyze(board, _boardSize, r, c, side);
            if (mine.MakesFive) return WinScore;

            var theirs = MediumAI.Analyze(board, _boardSize, r, c, opponent);
            if (theirs.MakesFive) return -WinScore;

            if (mine.Score > myBest) myBest = mine.Score;
            if (theirs.Score > oppBest) oppBest = theirs.Score;
            if (MediumAI.IsForcedWin(mine)) myForced = true;
            if (MediumAI.IsForcedWin(theirs)) oppForced = true;
        }

        long score = myBest - oppBest;
        if (myForced) score += 200_000;
        if (oppForced) score -= 200_000;
        return score;
    }

    private static List<Move> Order(List<Move> moves, int width)
    {
        return moves
            .OrderByDescending(m => m.Own.Score * 12 / 10 + m.Defense.Score)
            .Take(width)
            .ToList();
    }

    /// <summary>
    /// VCF：只走“冲四”（对手必须应一手）的强制序列，找到能杀死的路径。
    /// </summary>
    private (int row, int col)? FindVcfWin(CellState[] board, CellState attacker, CellState defender)
    {
        _clock.Restart();
        int nodes = 0;

        if (TryVcf(board, attacker, defender, VcfMaxDepth, ref nodes, out var move))
            return move;

        return null;
    }

    private bool TryVcf(CellState[] board, CellState attacker, CellState defender, int depth, ref int nodes,
        out (int row, int col)? move)
    {
        move = null;
        nodes++;

        if (depth <= 0 || nodes > VcfMaxNodes || _clock.ElapsedMilliseconds > VcfBudgetMs)
            return false;

        var candidates = MediumAI.GetCandidates(board, _boardSize);
        var fours = new List<(int Row, int Col, bool DoubleWin)>();

        foreach (var (r, c) in candidates)
        {
            var own = MediumAI.Analyze(board, _boardSize, r, c, attacker);

            if (own.MakesFive)
            {
                move = (r, c);
                return true;
            }

            if (own.WinningCells >= 1)
                fours.Add((r, c, own.WinningCells >= 2));
        }

        // 双活四/双四：对手堵不住
        foreach (var four in fours)
        {
            if (!four.DoubleWin) continue;

            move = (four.Row, four.Col);
            return true;
        }

        foreach (var four in fours)
        {
            int attackerIndex = four.Row * _boardSize + four.Col;
            board[attackerIndex] = attacker;

            var winningCells = MediumAI.FindWinningCells(board, _boardSize, four.Row, four.Col, attacker);

            if (winningCells.Count >= 2)
            {
                board[attackerIndex] = CellState.Empty;
                move = (four.Row, four.Col);
                return true;
            }

            if (winningCells.Count == 1)
            {
                int blockIndex = winningCells.First();
                int blockRow = blockIndex / _boardSize;
                int blockCol = blockIndex % _boardSize;

                board[blockIndex] = defender;

                // 对手这一挡若顺手造出四，这条 VCF 线路不再成立
                bool counterThreat = MediumAI.FindWinningCells(board, _boardSize, blockRow, blockCol, defender).Count > 0;

                bool win = false;
                if (!counterThreat)
                    win = TryVcf(board, attacker, defender, depth - 1, ref nodes, out _);

                board[blockIndex] = CellState.Empty;
                board[attackerIndex] = CellState.Empty;

                if (win)
                {
                    move = (four.Row, four.Col);
                    return true;
                }
            }
            else
            {
                board[attackerIndex] = CellState.Empty;
            }
        }

        return false;
    }

    private (int row, int col) PickRandom(List<Move> moves)
    {
        var pick = moves[Rng.Next(moves.Count)];
        return (pick.Row, pick.Col);
    }

    private (int row, int col) PickBest(List<Move> moves, Func<Move, long> selector)
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

    private static readonly Random Rng = new();

    private readonly record struct Move(int Row, int Col, MediumAI.MoveAnalysis Own, MediumAI.MoveAnalysis Defense);
}
