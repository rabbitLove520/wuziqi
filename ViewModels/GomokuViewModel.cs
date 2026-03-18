using System.Collections.ObjectModel;
using System.Windows.Input;
using WuziqiAi.MVVM;
using WuziqiAi.Models;
using WuziqiAi.AI;

namespace WuziqiAi.ViewModels;

public class GomokuViewModel : ViewModelBase
{
    public const int BoardSize = 15;
    private GomokuCell? _lastCell;

    public ObservableCollection<GomokuCell> Cells { get; } = new();

    private CellState _currentTurn = CellState.Black; // 黑棋先手
    public CellState CurrentTurn
    {
        get => _currentTurn;
        set => SetProperty(ref _currentTurn, value);
    }

    private string _statusMessage = "黑棋回合";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _isGameOver;
    public bool IsGameOver
    {
        get => _isGameOver;
        set => SetProperty(ref _isGameOver, value);
    }

    public ICommand CellClickCommand { get; }
    public ICommand ResetCommand { get; }

    private readonly SimpleAI _ai;

    public GomokuViewModel()
    {
        _ai = new SimpleAI(BoardSize);
        InitializeBoard();
        CellClickCommand = new RelayCommand(OnCellClick, _ => !IsGameOver);
        ResetCommand = new RelayCommand(_ => ResetGame());
    }

    private void InitializeBoard()
    {
        Cells.Clear();
        for (int r = 0; r < BoardSize; r++)
        {
            for (int c = 0; c < BoardSize; c++)
            {
                Cells.Add(new GomokuCell(r, c));
            }
        }
    }

    private void OnCellClick(object? parameter)
    {
        if (parameter is not GomokuCell cell || cell.State != CellState.Empty || IsGameOver)
            return;

        MakeMove(cell);

        if (!IsGameOver && CurrentTurn == CellState.White)
        {
            // AI 思考并落子
            var aiMove = _ai.GetBestMove(Cells, CellState.White);
            if (aiMove != null)
            {
                var targetCell = Cells[aiMove.Value.row * BoardSize + aiMove.Value.col];
                MakeMove(targetCell);
            }
        }
    }

    private void MakeMove(GomokuCell cell)
    {
        if (_lastCell != null) _lastCell.IsLastMove = false;
        
        cell.State = CurrentTurn;
        cell.IsLastMove = true;
        _lastCell = cell;

        if (CheckWin(cell.Row, cell.Column, CurrentTurn))
        {
            StatusMessage = $"{(CurrentTurn == CellState.Black ? "黑棋" : "白棋")} 获胜！";
            IsGameOver = true;
        }
        else if (Cells.All(c => c.State != CellState.Empty))
        {
            StatusMessage = "平局！";
            IsGameOver = true;
        }
        else
        {
            CurrentTurn = CurrentTurn == CellState.Black ? CellState.White : CellState.Black;
            StatusMessage = $"{(CurrentTurn == CellState.Black ? "黑棋" : "白棋")} 回合";
        }
    }

    private bool CheckWin(int row, int col, CellState state)
    {
        int[][] directions = {
            new[] { 1, 0 },  // Horizontal
            new[] { 0, 1 },  // Vertical
            new[] { 1, 1 },  // Diagonal \
            new[] { 1, -1 }  // Diagonal /
        };

        foreach (var dir in directions)
        {
            int count = 1;
            count += CountInDirection(row, col, dir[0], dir[1], state);
            count += CountInDirection(row, col, -dir[0], -dir[1], state);

            if (count >= 5) return true;
        }

        return false;
    }

    private int CountInDirection(int row, int col, int dr, int dc, CellState state)
    {
        int count = 0;
        int r = row + dr;
        int c = col + dc;

        while (r >= 0 && r < BoardSize && c >= 0 && c < BoardSize &&
               Cells[r * BoardSize + c].State == state)
        {
            count++;
            r += dr;
            c += dc;
        }
        return count;
    }

    private void ResetGame()
    {
        foreach (var cell in Cells)
        {
            cell.State = CellState.Empty;
            cell.IsLastMove = false;
        }
        _lastCell = null;
        CurrentTurn = CellState.Black;
        StatusMessage = "黑棋回合";
        IsGameOver = false;
    }
}
