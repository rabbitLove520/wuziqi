using WuziqiAi.MVVM;

namespace WuziqiAi.Models;

public enum CellState
{
    Empty,
    Black,
    White
}

public class GomokuCell : ViewModelBase
{
    public int Row { get; }
    public int Column { get; }

    private CellState _state = CellState.Empty;
    public CellState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    private bool _isLastMove;
    public bool IsLastMove
    {
        get => _isLastMove;
        set => SetProperty(ref _isLastMove, value);
    }

    public GomokuCell(int row, int col)
    {
        Row = row;
        Column = col;
    }
}
