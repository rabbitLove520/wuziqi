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

    private string _statusMessage = "点击「开始游戏」";
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

    private bool _isAiThinking;
    public bool IsAiThinking
    {
        get => _isAiThinking;
        set => SetProperty(ref _isAiThinking, value);
    }

    private Difficulty _selectedDifficulty = Difficulty.Easy;
    public Difficulty SelectedDifficulty
    {
        get => _selectedDifficulty;
        set
        {
            if (SetProperty(ref _selectedDifficulty, value))
            {
                OnPropertyChanged(nameof(IsEasySelected));
                OnPropertyChanged(nameof(IsMediumSelected));
                OnPropertyChanged(nameof(IsHardSelected));
            }
        }
    }

    public bool IsEasySelected
    {
        get => SelectedDifficulty == Difficulty.Easy;
        set
        {
            if (value) SelectedDifficulty = Difficulty.Easy;
        }
    }

    public bool IsMediumSelected
    {
        get => SelectedDifficulty == Difficulty.Medium;
        set
        {
            if (value) SelectedDifficulty = Difficulty.Medium;
        }
    }

    public bool IsHardSelected
    {
        get => SelectedDifficulty == Difficulty.Hard;
        set
        {
            if (value) SelectedDifficulty = Difficulty.Hard;
        }
    }

    private bool _isGameStarted;
    public bool IsGameStarted
    {
        get => _isGameStarted;
        private set
        {
            if (SetProperty(ref _isGameStarted, value))
                OnPropertyChanged(nameof(ActionButtonText));
        }
    }

    public string ActionButtonText =>
        !IsGameStarted ? "开始游戏" : IsGameOver ? "再来一局" : "重新开始";

    public ICommand CellClickCommand { get; }
    public ICommand ActionCommand { get; }

    private readonly SimpleAI _easyAi;
    private readonly MediumAI _mediumAi;
    private readonly HardAI _hardAi;
    private int _gameVersion;

    public GomokuViewModel()
    {
        _easyAi = new SimpleAI(BoardSize);
        _mediumAi = new MediumAI(BoardSize);
        _hardAi = new HardAI(BoardSize);
        InitializeBoard();
        CellClickCommand = new RelayCommand(OnCellClick, _ => IsGameStarted && !IsGameOver);
        ActionCommand = new RelayCommand(_ => OnActionButton());
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

    private void OnActionButton()
    {
        IsGameStarted = true;
        ResetGame();
        CommandManager.InvalidateRequerySuggested();
    }

    private async void OnCellClick(object? parameter)
    {
        if (parameter is not GomokuCell cell || cell.State != CellState.Empty || !IsGameStarted || IsGameOver || IsAiThinking)
            return;

        MakeMove(cell);

        if (IsGameOver || CurrentTurn != CellState.White)
            return;

        // 困难难度在后台线程搜索，避免卡住 UI
        IsAiThinking = true;
        int version = _gameVersion;

        try
        {
            (int row, int col)? aiMove;

            if (SelectedDifficulty == Difficulty.Hard)
            {
                var board = SnapshotBoard();
                aiMove = await Task.Run(() => _hardAi.GetBestMove(board, CellState.White));
            }
            else if (SelectedDifficulty == Difficulty.Medium)
            {
                aiMove = _mediumAi.GetBestMove(Cells, CellState.White);
            }
            else
            {
                aiMove = _easyAi.GetBestMove(Cells, CellState.White);
            }

            // 思考期间棋盘已被重开，丢弃这次结果
            if (version != _gameVersion || IsGameOver)
                return;

            if (aiMove != null)
            {
                var targetCell = Cells[aiMove.Value.row * BoardSize + aiMove.Value.col];
                MakeMove(targetCell);
            }
        }
        finally
        {
            IsAiThinking = false;
        }
    }

    private CellState[] SnapshotBoard()
    {
        var board = new CellState[Cells.Count];
        for (int i = 0; i < board.Length; i++)
            board[i] = Cells[i].State;

        return board;
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

        OnPropertyChanged(nameof(ActionButtonText));
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
        _gameVersion++;
        CurrentTurn = CellState.Black;
        StatusMessage = "黑棋回合";
        IsGameOver = false;
        OnPropertyChanged(nameof(ActionButtonText));
    }
}
