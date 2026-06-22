using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

public class BoardController : MonoBehaviour
{
    [Header("Board")]
    [SerializeField] private int rows = 8;
    [SerializeField] private int columns = 6;
    [SerializeField] private float cellSize = 1f;

    [Header("Game")]
    [SerializeField] private int lives = 3;
    [SerializeField] private int currentLevelIndex = 0;

    [Header("Animation")]
    [SerializeField] private float moveDuration = 0.25f;
    [SerializeField] private float bumpDistance = 0.25f;

    [Header("Level Editor")]
    [SerializeField] private int editorLevelId = 1;
    [SerializeField] private int editorRows = 8;
    [SerializeField] private int editorColumns = 6;
    [SerializeField] private int editorLives = 3;
    [SerializeField] private float doubleClickThreshold = 0.3f;

    private const float GameTopGuiHeight = 330f;
    private const float EditorTopGuiHeight = 360f;
    private const float BoardTopMarginWorld = 0.25f;

    private ArrowView[,] grid;
    private int arrowsLeft;
    private bool isBusy;
    private bool isGameOver;
    private bool isLevelCompleted;
    private bool isAllLevelsCompleted;
    private bool isEditorMode;

    private Sprite squareSprite;
    private TextAsset[] levelAssets;
    private ArrowView lastClickedEditorArrow;
    private float lastEditorClickTime;

    private void Start()
    {
        squareSprite = CreateSquareSprite();

        LoadLevelList();
        SetupCamera();
        GenerateLevel();
    }

    private void Update()
    {
        if (!TryGetPressedScreenPosition(out Vector2 screenPosition))
        {
            return;
        }

        if (isEditorMode)
        {
            TryHandleEditorClick(screenPosition);
            return;
        }

        if (isBusy || isGameOver || isLevelCompleted)
        {
            return;
        }

        TrySelectArrow(screenPosition);
    }

    private bool TryGetPressedScreenPosition(out Vector2 screenPosition)
    {
        screenPosition = Vector2.zero;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }

        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;

            if (touch.press.wasPressedThisFrame)
            {
                screenPosition = touch.position.ReadValue();
                return true;
            }
        }

        return false;
    }

    private void LoadLevelList()
    {
        levelAssets = Resources
            .LoadAll<TextAsset>("Levels")
            .Where(levelAsset => levelAsset.name.StartsWith("level_"))
            .OrderBy(levelAsset => levelAsset.name)
            .ToArray();

        Debug.Log($"Найдено уровней: {levelAssets.Length}");

        if (levelAssets.Length == 0)
        {
            Debug.LogError("В папке Resources/Levels не найдено JSON-файлов уровней.");
        }
    }

    private void TrySelectArrow(Vector2 screenPosition)
    {
        if (IsPointerOverTopGui(screenPosition))
        {
            return;
        }

        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return;
        }

        Vector3 worldPosition = mainCamera.ScreenToWorldPoint(screenPosition);
        Vector2 point = new Vector2(worldPosition.x, worldPosition.y);

        RaycastHit2D hit = Physics2D.Raycast(point, Vector2.zero);

        if (hit.collider == null)
        {
            return;
        }

        ArrowView arrow = hit.collider.GetComponent<ArrowView>();

        if (arrow == null)
        {
            return;
        }

        Debug.Log($"Нажата стрелка: {arrow.name}");

        OnArrowClicked(arrow);
    }

    public void OnArrowClicked(ArrowView arrow)
    {
        if (isBusy || isGameOver || isLevelCompleted || isEditorMode)
        {
            return;
        }

        if (IsPathClear(arrow))
        {
            StartCoroutine(MoveArrowOut(arrow));
        }
        else
        {
            lives--;
            Debug.Log($"Стрелка заблокирована. Осталось жизней: {lives}");

            StartCoroutine(BumpArrow(arrow));
        }
    }

    private void GenerateLevel()
    {
        LevelConfig levelConfig = LoadLevelConfig(currentLevelIndex);

        if (levelConfig == null)
        {
            Debug.LogError($"Не удалось загрузить уровень с индексом {currentLevelIndex}.");
            return;
        }

        rows = levelConfig.rows;
        columns = levelConfig.columns;
        lives = levelConfig.lives;

        grid = new ArrowView[rows, columns];
        arrowsLeft = 0;

        SetupCamera();
        CreateCells();
        CreateLevelArrows(levelConfig);

        Debug.Log($"Уровень {levelConfig.levelId} создан из JSON.");
    }

    private LevelConfig LoadLevelConfig(int levelIndex)
    {
        if (levelAssets == null || levelAssets.Length == 0)
        {
            Debug.LogError("Список уровней пуст.");
            return null;
        }

        if (levelIndex < 0 || levelIndex >= levelAssets.Length)
        {
            Debug.LogError($"Некорректный индекс уровня: {levelIndex}");
            return null;
        }

        TextAsset levelAsset = levelAssets[levelIndex];

        Debug.Log($"Загружается файл уровня: {levelAsset.name}");

        return JsonUtility.FromJson<LevelConfig>(levelAsset.text);
    }

    private void CreateLevelArrows(LevelConfig levelConfig)
    {
        if (levelConfig.arrows == null)
        {
            return;
        }

        foreach (ArrowConfig arrowConfig in levelConfig.arrows)
        {
            if (!TryParseDirection(arrowConfig.direction, out ArrowDirection direction))
            {
                Debug.LogError($"Некорректное направление стрелки: {arrowConfig.direction}");
                continue;
            }

            if (!IsInsideBoard(arrowConfig.row, arrowConfig.column))
            {
                Debug.LogError($"Стрелка вне поля: row={arrowConfig.row}, column={arrowConfig.column}");
                continue;
            }

            if (grid[arrowConfig.row, arrowConfig.column] != null)
            {
                Debug.LogError($"В клетке уже есть стрелка: row={arrowConfig.row}, column={arrowConfig.column}");
                continue;
            }

            PlaceArrow(arrowConfig.row, arrowConfig.column, direction);
        }
    }

    private bool TryParseDirection(string value, out ArrowDirection direction)
    {
        return System.Enum.TryParse(value, true, out direction);
    }

    private void CreateCells()
    {
        GameObject cellsRoot = new GameObject("Cells");
        cellsRoot.transform.SetParent(transform);

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                GameObject cell = new GameObject($"Cell_{row}_{column}");
                cell.transform.SetParent(cellsRoot.transform);
                cell.transform.position = GetWorldPosition(row, column);

                SpriteRenderer renderer = cell.AddComponent<SpriteRenderer>();
                renderer.sprite = squareSprite;
                renderer.color = new Color(0.25f, 0.25f, 0.25f, 0.35f);
                renderer.sortingOrder = 0;

                cell.transform.localScale = Vector3.one * 0.92f;
            }
        }
    }

    private void PlaceArrow(int row, int column, ArrowDirection direction)
    {
        GameObject arrowObject = new GameObject($"Arrow_{row}_{column}_{direction}");
        arrowObject.transform.SetParent(transform);
        arrowObject.transform.position = GetWorldPosition(row, column);

        SpriteRenderer background = arrowObject.AddComponent<SpriteRenderer>();
        background.sprite = squareSprite;
        background.color = new Color(0.15f, 0.45f, 0.85f, 1f);
        background.sortingOrder = 1;

        arrowObject.transform.localScale = Vector3.one * 0.82f;

        BoxCollider2D collider = arrowObject.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(arrowObject.transform);
        labelObject.transform.localPosition = Vector3.zero;

        TextMesh textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.12f;
        textMesh.fontSize = 80;
        textMesh.color = Color.white;

        MeshRenderer labelRenderer = labelObject.GetComponent<MeshRenderer>();
        labelRenderer.sortingOrder = 2;

        ArrowView arrowView = arrowObject.AddComponent<ArrowView>();
        arrowView.Init(this, row, column, direction, textMesh);

        grid[row, column] = arrowView;
        arrowsLeft++;
    }

    private bool IsPathClear(ArrowView arrow)
    {
        Vector2Int direction = GetDirectionVector(arrow.Direction);

        int row = arrow.Row + direction.y;
        int column = arrow.Column + direction.x;

        while (IsInsideBoard(row, column))
        {
            if (grid[row, column] != null)
            {
                return false;
            }

            row += direction.y;
            column += direction.x;
        }

        return true;
    }

    private IEnumerator MoveArrowOut(ArrowView arrow)
    {
        isBusy = true;
        arrow.SetInteractable(false);

        grid[arrow.Row, arrow.Column] = null;

        Vector3 startPosition = arrow.transform.position;
        Vector3 targetPosition = GetExitPosition(arrow);

        float time = 0f;

        while (time < moveDuration)
        {
            time += Time.deltaTime;
            float progress = time / moveDuration;

            arrow.transform.position = Vector3.Lerp(startPosition, targetPosition, progress);

            yield return null;
        }

        Destroy(arrow.gameObject);

        arrowsLeft--;

        if (arrowsLeft <= 0)
        {
            isLevelCompleted = true;
            Debug.Log("Победа. Все стрелки выведены за поле.");
        }

        isBusy = false;
    }

    private IEnumerator BumpArrow(ArrowView arrow)
    {
        isBusy = true;
        arrow.SetInteractable(false);

        Vector3 startPosition = arrow.transform.position;
        Vector2Int direction = GetDirectionVector(arrow.Direction);
        Vector3 bumpPosition = startPosition + new Vector3(direction.x, direction.y, 0f) * bumpDistance;

        float halfDuration = moveDuration / 2f;
        float time = 0f;

        while (time < halfDuration)
        {
            time += Time.deltaTime;
            float progress = time / halfDuration;

            arrow.transform.position = Vector3.Lerp(startPosition, bumpPosition, progress);

            yield return null;
        }

        time = 0f;

        while (time < halfDuration)
        {
            time += Time.deltaTime;
            float progress = time / halfDuration;

            arrow.transform.position = Vector3.Lerp(bumpPosition, startPosition, progress);

            yield return null;
        }

        arrow.transform.position = startPosition;

        if (lives <= 0)
        {
            isGameOver = true;
            Debug.Log("Поражение. Жизни закончились.");
        }
        else
        {
            arrow.SetInteractable(true);
        }

        isBusy = false;
    }

    private Vector3 GetExitPosition(ArrowView arrow)
    {
        Vector2Int direction = GetDirectionVector(arrow.Direction);

        int row = arrow.Row;
        int column = arrow.Column;

        while (IsInsideBoard(row, column))
        {
            row += direction.y;
            column += direction.x;
        }

        return GetWorldPosition(row, column);
    }

    private Vector2Int GetDirectionVector(ArrowDirection direction)
    {
        return direction switch
        {
            ArrowDirection.Up => new Vector2Int(0, -1),
            ArrowDirection.Down => new Vector2Int(0, 1),
            ArrowDirection.Left => new Vector2Int(-1, 0),
            ArrowDirection.Right => new Vector2Int(1, 0),
            _ => Vector2Int.zero
        };
    }

    private ArrowDirection GetNextDirection(ArrowDirection direction)
    {
        return direction switch
        {
            ArrowDirection.Up => ArrowDirection.Right,
            ArrowDirection.Right => ArrowDirection.Down,
            ArrowDirection.Down => ArrowDirection.Left,
            ArrowDirection.Left => ArrowDirection.Up,
            _ => ArrowDirection.Up
        };
    }

    private bool IsInsideBoard(int row, int column)
    {
        return row >= 0 && row < rows && column >= 0 && column < columns;
    }

    private Vector3 GetWorldPosition(int row, int column)
    {
        float x = (column - (columns - 1) / 2f) * cellSize;
        float y = ((rows - 1) / 2f - row) * cellSize;

        return new Vector3(x, y, 0f);
    }

    private bool TryGetCellFromWorldPosition(Vector3 worldPosition, out int row, out int column)
    {
        float columnFloat = worldPosition.x / cellSize + (columns - 1) / 2f;
        float rowFloat = (rows - 1) / 2f - worldPosition.y / cellSize;

        column = Mathf.RoundToInt(columnFloat);
        row = Mathf.RoundToInt(rowFloat);

        if (!IsInsideBoard(row, column))
        {
            return false;
        }

        Vector3 cellCenter = GetWorldPosition(row, column);
        float halfCellSize = cellSize / 2f;

        return Mathf.Abs(worldPosition.x - cellCenter.x) <= halfCellSize &&
               Mathf.Abs(worldPosition.y - cellCenter.y) <= halfCellSize;
    }

    private void SetupCamera()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return;
        }

        mainCamera.orthographic = true;

        float aspect = Mathf.Max(0.1f, mainCamera.aspect);
        float verticalSize = rows * cellSize / 2f + 1.2f;
        float horizontalSize = columns * cellSize / (2f * aspect) + 0.4f;
        mainCamera.orthographicSize = Mathf.Max(verticalSize, horizontalSize);

        float cameraY = 0f;

        if (isEditorMode)
        {
            float screenHeight = Mathf.Max(1f, Screen.height);
            float panelFraction = Mathf.Clamp01(EditorTopGuiHeight / screenHeight);
            float boardTop = rows * cellSize / 2f;

            cameraY = boardTop + BoardTopMarginWorld - mainCamera.orthographicSize +
                      2f * mainCamera.orthographicSize * panelFraction;
        }

        mainCamera.transform.position = new Vector3(0f, cameraY, -10f);
    }

    private Sprite CreateSquareSprite()
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f),
            1f
        );
    }

    private void RestartLevel()
    {
        StopAllCoroutines();

        ClearBoard();

        lives = 3;
        arrowsLeft = 0;
        isBusy = false;
        isGameOver = false;
        isLevelCompleted = false;
        isAllLevelsCompleted = false;
        isEditorMode = false;

        GenerateLevel();

        Debug.Log("Уровень перезапущен.");
    }

    private void NextLevel()
    {
        if (currentLevelIndex >= levelAssets.Length - 1)
        {
            isAllLevelsCompleted = true;
            Debug.Log("Все уровни пройдены.");
            return;
        }

        currentLevelIndex++;

        ClearBoard();

        lives = 3;
        arrowsLeft = 0;
        isBusy = false;
        isGameOver = false;
        isLevelCompleted = false;
        isAllLevelsCompleted = false;
        isEditorMode = false;

        GenerateLevel();

        Debug.Log($"Переход на уровень {currentLevelIndex + 1}.");
    }

    private void EnterEditorMode()
    {
        StopAllCoroutines();

        isEditorMode = true;
        isBusy = false;
        isGameOver = false;
        isLevelCompleted = false;
        isAllLevelsCompleted = false;

        RebuildEditorBoard();

        Debug.Log("Включен режим редактора уровня.");
    }

    private void ExitEditorMode()
    {
        StopAllCoroutines();
        ClearBoard();

        isEditorMode = false;
        isBusy = false;
        isGameOver = false;
        isLevelCompleted = false;
        isAllLevelsCompleted = false;

        GenerateLevel();

        Debug.Log("Выход из редактора уровня.");
    }

    private void RebuildEditorBoard()
    {
        ClearBoard();

        rows = editorRows;
        columns = editorColumns;
        lives = editorLives;
        arrowsLeft = 0;
        grid = new ArrowView[rows, columns];
        lastClickedEditorArrow = null;
        lastEditorClickTime = 0f;

        SetupCamera();
        CreateCells();
    }

    private void ClearBoard()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }
    }

    private void TryHandleEditorClick(Vector2 screenPosition)
    {
        if (IsPointerOverTopGui(screenPosition))
        {
            return;
        }

        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return;
        }

        Vector3 worldPosition = mainCamera.ScreenToWorldPoint(screenPosition);
        Vector2 point = new Vector2(worldPosition.x, worldPosition.y);

        RaycastHit2D hit = Physics2D.Raycast(point, Vector2.zero);
        ArrowView arrow = hit.collider != null ? hit.collider.GetComponent<ArrowView>() : null;

        if (arrow != null)
        {
            bool isDoubleClick = lastClickedEditorArrow == arrow &&
                                 Time.unscaledTime - lastEditorClickTime <= doubleClickThreshold;

            if (isDoubleClick)
            {
                DeleteEditorArrow(arrow);
                lastClickedEditorArrow = null;
                lastEditorClickTime = 0f;
                return;
            }

            RotateEditorArrow(arrow);
            lastClickedEditorArrow = arrow;
            lastEditorClickTime = Time.unscaledTime;
            return;
        }

        if (!TryGetCellFromWorldPosition(worldPosition, out int row, out int column))
        {
            return;
        }

        if (grid[row, column] != null)
        {
            return;
        }

        PlaceArrow(row, column, ArrowDirection.Up);
        lastClickedEditorArrow = null;
        lastEditorClickTime = 0f;

        Debug.Log($"Создана стрелка: row={row}, column={column}, direction=Up");
    }

    private void RotateEditorArrow(ArrowView arrow)
    {
        ArrowDirection nextDirection = GetNextDirection(arrow.Direction);
        arrow.SetDirection(nextDirection);

        Debug.Log($"Стрелка повернута: row={arrow.Row}, column={arrow.Column}, direction={nextDirection}");
    }

    private void DeleteEditorArrow(ArrowView arrow)
    {
        if (grid[arrow.Row, arrow.Column] == arrow)
        {
            grid[arrow.Row, arrow.Column] = null;
        }

        Destroy(arrow.gameObject);
        arrowsLeft = Mathf.Max(0, arrowsLeft - 1);

        Debug.Log($"Стрелка удалена: row={arrow.Row}, column={arrow.Column}");
    }

    private bool IsPointerOverTopGui(Vector2 screenPosition)
    {
        float guiY = Screen.height - screenPosition.y;
        float topGuiHeight = isEditorMode ? EditorTopGuiHeight : GameTopGuiHeight;

        return guiY <= topGuiHeight;
    }

    private void ExportEditorJson()
    {
        string json = BuildEditorJson();
        GUIUtility.systemCopyBuffer = json;

        Debug.Log($"JSON уровня скопирован в буфер обмена:\n{json}");
    }

    private string BuildEditorJson()
    {
        List<ArrowConfig> arrowConfigs = new List<ArrowConfig>();

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                ArrowView arrow = grid[row, column];

                if (arrow == null)
                {
                    continue;
                }

                arrowConfigs.Add(new ArrowConfig
                {
                    row = row,
                    column = column,
                    direction = arrow.Direction.ToString()
                });
            }
        }

        LevelConfig levelConfig = new LevelConfig
        {
            levelId = editorLevelId,
            rows = rows,
            columns = columns,
            lives = editorLives,
            arrows = arrowConfigs.ToArray()
        };

        return JsonUtility.ToJson(levelConfig, true);
    }

    private void OnGUI()
    {
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 36;
        labelStyle.normal.textColor = Color.white;

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 32;

        if (isEditorMode)
        {
            DrawEditorGui(labelStyle, buttonStyle);
            return;
        }

        GUI.Label(new Rect(30, 30, 400, 60), $"Level: {currentLevelIndex + 1}", labelStyle);
        GUI.Label(new Rect(30, 90, 400, 60), $"Lives: {lives}", labelStyle);
        GUI.Label(new Rect(30, 150, 400, 60), $"Arrows left: {arrowsLeft}", labelStyle);

        if (GUI.Button(new Rect(30, 220, 220, 70), "Restart", buttonStyle))
        {
            RestartLevel();
        }

        if (GUI.Button(new Rect(Screen.width - 250, 30, 220, 70), "Editor", buttonStyle))
        {
            EnterEditorMode();
        }

        if (isAllLevelsCompleted)
        {
            GUI.Box(new Rect(Screen.width / 2 - 280, Screen.height / 2 - 120, 560, 240), "");

            GUIStyle resultStyle = new GUIStyle(GUI.skin.label);
            resultStyle.fontSize = 38;
            resultStyle.alignment = TextAnchor.MiddleCenter;
            resultStyle.normal.textColor = Color.white;

            GUI.Label(
                new Rect(Screen.width / 2 - 280, Screen.height / 2 - 90, 560, 100),
                "All Levels Complete",
                resultStyle
            );

            if (GUI.Button(
                new Rect(Screen.width / 2 - 120, Screen.height / 2 + 30, 240, 70),
                "Restart",
                buttonStyle
            ))
            {
                currentLevelIndex = 0;
                RestartLevel();
            }

            return;
        }

        if (isLevelCompleted)
        {
            GUI.Box(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 120, 500, 240), "");

            GUIStyle resultStyle = new GUIStyle(GUI.skin.label);
            resultStyle.fontSize = 42;
            resultStyle.alignment = TextAnchor.MiddleCenter;
            resultStyle.normal.textColor = Color.white;

            GUI.Label(
                new Rect(Screen.width / 2 - 250, Screen.height / 2 - 90, 500, 80),
                "Level Complete",
                resultStyle
            );

            if (GUI.Button(
                new Rect(Screen.width / 2 - 250, Screen.height / 2 + 20, 220, 70),
                "Restart",
                buttonStyle
            ))
            {
                RestartLevel();
            }

            if (GUI.Button(
                new Rect(Screen.width / 2 + 30, Screen.height / 2 + 20, 220, 70),
                "Next",
                buttonStyle
            ))
            {
                NextLevel();
            }
        }

        if (isGameOver)
        {
            GUI.Box(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 100, 500, 200), "");

            GUIStyle resultStyle = new GUIStyle(GUI.skin.label);
            resultStyle.fontSize = 42;
            resultStyle.alignment = TextAnchor.MiddleCenter;
            resultStyle.normal.textColor = Color.white;

            GUI.Label(
                new Rect(Screen.width / 2 - 250, Screen.height / 2 - 80, 500, 80),
                "Game Over",
                resultStyle
            );

            if (GUI.Button(
                new Rect(Screen.width / 2 - 120, Screen.height / 2 + 20, 240, 70),
                "Restart",
                buttonStyle
            ))
            {
                RestartLevel();
            }
        }
    }

    private void DrawEditorGui(GUIStyle labelStyle, GUIStyle buttonStyle)
    {
        GUI.Box(new Rect(0, 0, Screen.width, EditorTopGuiHeight), "");

        GUIStyle editorLabelStyle = new GUIStyle(labelStyle);
        editorLabelStyle.fontSize = 28;

        GUIStyle smallButtonStyle = new GUIStyle(buttonStyle);
        smallButtonStyle.fontSize = 24;

        GUIStyle hintStyle = new GUIStyle(GUI.skin.label);
        hintStyle.fontSize = 20;
        hintStyle.normal.textColor = Color.white;

        GUI.Label(new Rect(30, 25, 500, 45), "EDITOR MODE", editorLabelStyle);
        GUI.Label(new Rect(30, 75, 260, 45), $"Level ID: {editorLevelId}", editorLabelStyle);
        GUI.Label(new Rect(30, 130, 260, 45), $"Rows: {editorRows}", editorLabelStyle);
        GUI.Label(new Rect(30, 185, 260, 45), $"Columns: {editorColumns}", editorLabelStyle);
        GUI.Label(new Rect(30, 240, 260, 45), $"Lives: {editorLives}", editorLabelStyle);

        if (GUI.Button(new Rect(270, 75, 55, 45), "-", smallButtonStyle))
        {
            editorLevelId = Mathf.Max(1, editorLevelId - 1);
        }

        if (GUI.Button(new Rect(335, 75, 55, 45), "+", smallButtonStyle))
        {
            editorLevelId++;
        }

        if (GUI.Button(new Rect(270, 130, 55, 45), "-", smallButtonStyle))
        {
            editorRows = Mathf.Max(3, editorRows - 1);
            RebuildEditorBoard();
        }

        if (GUI.Button(new Rect(335, 130, 55, 45), "+", smallButtonStyle))
        {
            editorRows++;
            RebuildEditorBoard();
        }

        if (GUI.Button(new Rect(270, 185, 55, 45), "-", smallButtonStyle))
        {
            editorColumns = Mathf.Max(3, editorColumns - 1);
            RebuildEditorBoard();
        }

        if (GUI.Button(new Rect(335, 185, 55, 45), "+", smallButtonStyle))
        {
            editorColumns++;
            RebuildEditorBoard();
        }

        if (GUI.Button(new Rect(270, 240, 55, 45), "-", smallButtonStyle))
        {
            editorLives = Mathf.Max(1, editorLives - 1);
            lives = editorLives;
        }

        if (GUI.Button(new Rect(335, 240, 55, 45), "+", smallButtonStyle))
        {
            editorLives++;
            lives = editorLives;
        }

        if (GUI.Button(new Rect(Screen.width - 520, 75, 140, 60), "Clear", smallButtonStyle))
        {
            RebuildEditorBoard();
        }

        if (GUI.Button(new Rect(Screen.width - 365, 75, 190, 60), "Export JSON", smallButtonStyle))
        {
            ExportEditorJson();
        }

        if (GUI.Button(new Rect(Screen.width - 160, 75, 130, 60), "Back", smallButtonStyle))
        {
            ExitEditorMode();
        }

        GUI.Label(
            new Rect(30, 300, Screen.width - 60, 40),
            "Click empty cell - create Up arrow. Click arrow - rotate. Double click arrow - delete. Export JSON copies level to clipboard.",
            hintStyle
        );
    }
}
