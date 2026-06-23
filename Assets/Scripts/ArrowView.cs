using System.Collections.Generic;
using UnityEngine;

public class ArrowView : MonoBehaviour
{
    public int Row { get; private set; }
    public int Column { get; private set; }
    public ArrowDirection Direction { get; private set; }
    public IReadOnlyList<Vector2Int> OccupiedCells => occupiedCells;
    public IReadOnlyList<Vector2Int> BodyCells => bodyCells;

    private readonly List<Vector2Int> occupiedCells = new List<Vector2Int>();
    private readonly List<Vector2Int> bodyCells = new List<Vector2Int>();

    private BoardController boardController;
    private TextMesh headLabel;
    private bool isLocked;

    public void Init(
        BoardController controller,
        int row,
        int column,
        ArrowDirection direction,
        List<Vector2Int> bodyCellPositions,
        TextMesh arrowHeadLabel
    )
    {
        boardController = controller;
        Row = row;
        Column = column;
        Direction = direction;
        headLabel = arrowHeadLabel;

        occupiedCells.Clear();
        bodyCells.Clear();

        Vector2Int headCell = new Vector2Int(column, row);
        occupiedCells.Add(headCell);

        if (bodyCellPositions != null)
        {
            foreach (Vector2Int bodyCell in bodyCellPositions)
            {
                bodyCells.Add(bodyCell);
                occupiedCells.Add(bodyCell);
            }
        }

        name = $"Arrow_{row}_{column}_{direction}";
        SetLabel(direction);
    }

    public void SetInteractable(bool value)
    {
        isLocked = !value;
    }

    public void SetDirection(ArrowDirection direction)
    {
        Direction = direction;
        name = $"Arrow_{Row}_{Column}_{Direction}";
        SetLabel(direction);
    }

    public bool ContainsCell(int row, int column)
    {
        Vector2Int cell = new Vector2Int(column, row);
        return occupiedCells.Contains(cell);
    }

    private void OnMouseDown()
    {
        if (isLocked)
        {
            return;
        }

        if (boardController == null)
        {
            return;
        }

        boardController.OnArrowClicked(this);
    }

    private void SetLabel(ArrowDirection direction)
    {
        if (headLabel == null)
        {
            return;
        }

        headLabel.text = direction switch
        {
            ArrowDirection.Up => "↑",
            ArrowDirection.Down => "↓",
            ArrowDirection.Left => "←",
            ArrowDirection.Right => "→",
            _ => "?"
        };
    }
}
