using System.Collections.ObjectModel;
using WuziqiAi.Models;

namespace WuziqiAi.AI;

public class SimpleAI
{
    private readonly int _boardSize;

    public SimpleAI(int boardSize)
    {
        _boardSize = boardSize;
    }

    public (int row, int col)? GetBestMove(ObservableCollection<GomokuCell> cells, CellState aiState)
    {
        CellState playerState = aiState == CellState.Black ? CellState.White : CellState.Black;
        long bestScore = long.MinValue;
        List<(int row, int col)> bestMoves = new();

        // Only search near existing stones to save time and improve focus
        var searchRange = GetSearchRange(cells);

        foreach (var (r, c) in searchRange)
        {
            if (cells[r * _boardSize + c].State == CellState.Empty)
            {
                long score = EvaluateMove(cells, r, c, aiState, playerState);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMoves.Clear();
                    bestMoves.Add((r, c));
                }
                else if (score == bestScore)
                {
                    bestMoves.Add((r, c));
                }
            }
        }

        if (bestMoves.Count == 0) return (_boardSize / 2, _boardSize / 2);
        
        var rand = new Random();
        return bestMoves[rand.Next(bestMoves.Count)];
    }

    private List<(int row, int col)> GetSearchRange(ObservableCollection<GomokuCell> cells)
    {
        var range = new HashSet<(int, int)>();
        bool boardEmpty = true;

        for (int r = 0; r < _boardSize; r++)
        {
            for (int c = 0; c < _boardSize; c++)
            {
                if (cells[r * _boardSize + c].State != CellState.Empty)
                {
                    boardEmpty = false;
                    for (int dr = -2; dr <= 2; dr++)
                    {
                        for (int dc = -2; dc <= 2; dc++)
                        {
                            int nr = r + dr, nc = c + dc;
                            if (nr >= 0 && nr < _boardSize && nc >= 0 && nc < _boardSize && cells[nr * _boardSize + nc].State == CellState.Empty)
                                range.Add((nr, nc));
                        }
                    }
                }
            }
        }

        if (boardEmpty) return new List<(int, int)> { (_boardSize / 2, _boardSize / 2) };
        return range.ToList();
    }

    private long EvaluateMove(ObservableCollection<GomokuCell> cells, int row, int col, CellState aiState, CellState playerState)
    {
        // Attack is slightly more important than defense if scores are equal
        return EvaluateColor(cells, row, col, aiState) * 11 / 10 + EvaluateColor(cells, row, col, playerState);
    }

    private long EvaluateColor(ObservableCollection<GomokuCell> cells, int row, int col, CellState state)
    {
        long totalScore = 0;
        int[][] directions = { new[] { 1, 0 }, new[] { 0, 1 }, new[] { 1, 1 }, new[] { 1, -1 } };

        foreach (var dir in directions)
        {
            totalScore += GetLineScore(cells, row, col, dir[0], dir[1], state);
        }
        return totalScore;
    }

    private long GetLineScore(ObservableCollection<GomokuCell> cells, int row, int col, int dr, int dc, CellState state)
    {
        int count = 1;
        int block = 0;

        // Count in one direction
        int r = row + dr;
        int c = col + dc;
        while (r >= 0 && r < _boardSize && c >= 0 && c < _boardSize && cells[r * _boardSize + c].State == state)
        {
            count++;
            r += dr;
            c += dc;
        }
        if (!(r >= 0 && r < _boardSize && c >= 0 && c < _boardSize && cells[r * _boardSize + c].State == CellState.Empty))
            block++;

        // Count in opposite direction
        r = row - dr;
        c = col - dc;
        while (r >= 0 && r < _boardSize && c >= 0 && c < _boardSize && cells[r * _boardSize + c].State == state)
        {
            count++;
            r -= dr;
            c -= dc;
        }
        if (!(r >= 0 && r < _boardSize && c >= 0 && c < _boardSize && cells[r * _boardSize + c].State == CellState.Empty))
            block++;

        return ScorePattern(count, block);
    }

    private long ScorePattern(int count, int block)
    {
        if (count >= 5) return 10000000; // Win
        if (block == 2) return 0; // Blocked at both ends

        return (count, block) switch
        {
            (4, 0) => 1000000, // Live 4
            (4, 1) => 100000,  // Dead 4
            (3, 0) => 100000,  // Live 3
            (3, 1) => 1000,    // Dead 3
            (2, 0) => 1000,    // Live 2
            (2, 1) => 100,     // Dead 2
            (1, 0) => 100,
            _ => 10
        };
    }
}
