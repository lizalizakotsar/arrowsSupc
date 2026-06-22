using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class ArrowView : MonoBehaviour
{
    public int Row { get; private set; }
    public int Column { get; private set; }
    public ArrowDirection Direction { get; private set; }

    private BoardController boardController;
    private TextMesh label;
    private bool isLocked;

    public void Init(
        BoardController controller,
        int row,
        int column,
        ArrowDirection direction,
        TextMesh arrowLabel
    )
    {
        boardController = controller;
        Row = row;
        Column = column;
        Direction = direction;
        label = arrowLabel;

        name = $"Arrow_{row}_{column}_{direction}";
        SetLabel(direction);
    }

    public void SetInteractable(bool value)
    {
        isLocked = !value;
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
        if (label == null)
        {
            return;
        }

        label.text = direction switch
        {
            ArrowDirection.Up => "↑",
            ArrowDirection.Down => "↓",
            ArrowDirection.Left => "←",
            ArrowDirection.Right => "→",
            _ => "?"
        };
    }
}