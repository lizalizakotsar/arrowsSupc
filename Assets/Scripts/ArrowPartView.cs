using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class ArrowPartView : MonoBehaviour
{
    public ArrowView Arrow { get; private set; }
    public int Row { get; private set; }
    public int Column { get; private set; }

    public void Init(ArrowView arrow, int row, int column)
    {
        Arrow = arrow;
        Row = row;
        Column = column;
    }
}
