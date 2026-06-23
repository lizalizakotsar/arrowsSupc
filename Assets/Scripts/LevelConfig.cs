using System;
using UnityEngine;

[Serializable]
public class LevelConfig
{
    public int levelId;
    public int rows;
    public int columns;
    public int lives;
    public ArrowConfig[] arrows;
}

[Serializable]
public class ArrowConfig
{
    public int row;
    public int column;
    public string direction;
    public ArrowCellConfig[] body;
}

[Serializable]
public class ArrowCellConfig
{
    public int row;
    public int column;
}
