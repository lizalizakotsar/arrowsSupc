using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
using UnityEditor;
#endif

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
    private const float EditorTopGuiHeight = 400f;

    private ArrowView[,] grid;
    private int arrowsLeft;
    private bool isBusy;
    private bool isGameOver;
    private bool isLevelCompleted;
    private bool isAllLevelsCompleted;
    private bool isEditorMode;
    private bool isLevelSelectMenuOpen;

    private Sprite squareSprite;
    private TextAsset[] levelAssets;
    private ArrowView lastClickedEditorArrow;
    private float lastEditorClickTime;
    private int selectedEditorLevelIndex;
    private string selectedEditorLevelName = "No levels";
    private string loadedEditorLevelName;
    private bool isEditingNewEditorLevel;

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

        if (isLevelSelectMenuOpen)
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

        selectedEditorLevelIndex = Mathf.Clamp(selectedEditorLevelIndex, 0, Mathf.Max(0, levelAssets.Length - 1));
        UpdateSelectedEditorLevelName();
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
        if (isBusy || isGameOver || isLevelCompleted || isEditorMode || isLevelSelectMenuOpen)
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

        float topPanelFraction = isEditorMode
            ? Mathf.Clamp01(EditorTopGuiHeight / Mathf.Max(1f, Screen.height))
            : 0f;

        float cameraHeightFraction = Mathf.Max(0.1f, 1f - topPanelFraction);
        mainCamera.rect = new Rect(0f, 0f, 1f, cameraHeightFraction);

        float viewportAspect = Mathf.Max(0.1f, Screen.width / (Screen.height * cameraHeightFraction));
        float verticalSize = rows * cellSize / 2f + 0.8f;
        float horizontalSize = columns * cellSize / (2f * viewportAspect) + 0.5f;

        mainCamera.orthographicSize = Mathf.Max(verticalSize, horizontalSize);
        mainCamera.transform.position = new Vector3(0f, 0f, -10f);
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
        isLevelSelectMenuOpen = false;

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
        isLevelSelectMenuOpen = false;

        GenerateLevel();

        Debug.Log($"Переход на уровень {currentLevelIndex + 1}.");
    }

    private void OpenLevelSelectMenu()
    {
        LoadLevelList();

        StopAllCoroutines();

        isLevelSelectMenuOpen = true;
        isBusy = false;
        isGameOver = false;
        isLevelCompleted = false;
        isAllLevelsCompleted = false;

        Debug.Log("Открыто меню выбора уровня.");
    }

    private void CloseLevelSelectMenu()
    {
        isLevelSelectMenuOpen = false;
        Debug.Log("Закрыто меню выбора уровня.");
    }

    private void LoadGameLevel(int levelIndex)
    {
        if (levelAssets == null || levelAssets.Length == 0)
        {
            Debug.LogError("Нет уровней для загрузки.");
            return;
        }

        if (levelIndex < 0 || levelIndex >= levelAssets.Length)
        {
            Debug.LogError($"Некорректный индекс уровня: {levelIndex}");
            return;
        }

        StopAllCoroutines();
        ClearBoard();

        currentLevelIndex = levelIndex;
        lives = 3;
        arrowsLeft = 0;
        isBusy = false;
        isGameOver = false;
        isLevelCompleted = false;
        isAllLevelsCompleted = false;
        isEditorMode = false;
        isLevelSelectMenuOpen = false;

        GenerateLevel();

        Debug.Log($"Выбран уровень: {levelAssets[levelIndex].name}");
    }

    private void EnterEditorMode()
    {
        StopAllCoroutines();

        isEditorMode = true;
        isLevelSelectMenuOpen = false;
        isBusy = false;
        isGameOver = false;
        isLevelCompleted = false;
        isAllLevelsCompleted = false;

        if (levelAssets != null && levelAssets.Length > 0)
        {
            selectedEditorLevelIndex = Mathf.Clamp(currentLevelIndex, 0, levelAssets.Length - 1);
            LoadSelectedEditorLevel();
        }
        else
        {
            CreateNewEditorLevel();
        }

        Debug.Log("Включен режим редактора уровня.");
    }

    private void ExitEditorMode()
    {
        StopAllCoroutines();
        ClearBoard();

        isEditorMode = false;
        isLevelSelectMenuOpen = false;
        isBusy = false;
        isGameOver = false;
        isLevelCompleted = false;
        isAllLevelsCompleted = false;

        GenerateLevel();

        Debug.Log("Выход из редактора уровня.");
    }

    private void SelectPreviousEditorLevel()
    {
        if (levelAssets == null || levelAssets.Length == 0)
        {
            return;
        }

        selectedEditorLevelIndex--;

        if (selectedEditorLevelIndex < 0)
        {
            selectedEditorLevelIndex = levelAssets.Length - 1;
        }

        UpdateSelectedEditorLevelName();
    }

    private void SelectNextEditorLevel()
    {
        if (levelAssets == null || levelAssets.Length == 0)
        {
            return;
        }

        selectedEditorLevelIndex++;

        if (selectedEditorLevelIndex >= levelAssets.Length)
        {
            selectedEditorLevelIndex = 0;
        }

        UpdateSelectedEditorLevelName();
    }

    private void UpdateSelectedEditorLevelName()
    {
        if (levelAssets == null || levelAssets.Length == 0)
        {
            selectedEditorLevelName = "No levels";
            return;
        }

        selectedEditorLevelIndex = Mathf.Clamp(selectedEditorLevelIndex, 0, levelAssets.Length - 1);
        selectedEditorLevelName = levelAssets[selectedEditorLevelIndex].name;
    }

    private void LoadSelectedEditorLevel()
    {
        if (levelAssets == null || levelAssets.Length == 0)
        {
            Debug.LogError("Нет доступных уровней для загрузки в редактор.");
            CreateNewEditorLevel();
            return;
        }

        selectedEditorLevelIndex = Mathf.Clamp(selectedEditorLevelIndex, 0, levelAssets.Length - 1);
        UpdateSelectedEditorLevelName();

        LevelConfig levelConfig = LoadLevelConfig(selectedEditorLevelIndex);

        if (levelConfig == null)
        {
            Debug.LogError($"Не удалось загрузить выбранный уровень в редактор: {selectedEditorLevelName}");
            return;
        }

        ClearBoard();

        loadedEditorLevelName = selectedEditorLevelName;
        isEditingNewEditorLevel = false;

        editorLevelId = Mathf.Max(1, levelConfig.levelId);
        editorRows = Mathf.Max(3, levelConfig.rows);
        editorColumns = Mathf.Max(3, levelConfig.columns);
        editorLives = Mathf.Max(1, levelConfig.lives);

        rows = editorRows;
        columns = editorColumns;
        lives = editorLives;
        arrowsLeft = 0;
        grid = new ArrowView[rows, columns];
        lastClickedEditorArrow = null;
        lastEditorClickTime = 0f;

        SetupCamera();
        CreateCells();
        CreateLevelArrows(levelConfig);

        Debug.Log($"Уровень загружен в редактор: {selectedEditorLevelName}");
    }

    private void CreateNewEditorLevel()
    {
        editorLevelId = GetNextAvailableLevelId();
        editorRows = 8;
        editorColumns = 6;
        editorLives = 3;
        loadedEditorLevelName = null;
        isEditingNewEditorLevel = true;
        selectedEditorLevelName = "New level";

        RebuildEditorBoard();

        Debug.Log($"Создан новый уровень в редакторе. Файл для сохранения: {GetEditorSaveLevelName()}.json");
    }

    private int GetNextAvailableLevelId()
    {
        int maxLevelId = 0;

        if (levelAssets != null)
        {
            foreach (TextAsset levelAsset in levelAssets)
            {
                if (levelAsset == null)
                {
                    continue;
                }

                string numberPart = levelAsset.name.Replace("level_", "");

                if (int.TryParse(numberPart, out int fileLevelId))
                {
                    maxLevelId = Mathf.Max(maxLevelId, fileLevelId);
                }
            }
        }

        return Mathf.Max(1, maxLevelId + 1);
    }

    private int FindLevelIndexByName(string levelName)
    {
        if (levelAssets == null || levelAssets.Length == 0)
        {
            return -1;
        }

        for (int i = 0; i < levelAssets.Length; i++)
        {
            if (levelAssets[i].name == levelName)
            {
                return i;
            }
        }

        return -1;
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

    private void SaveEditorJsonToFile()
    {
#if UNITY_EDITOR
        string levelName = GetEditorSaveLevelName();
        string directoryPath = Path.Combine(Application.dataPath, "Resources", "Levels");
        string filePath = Path.Combine(directoryPath, $"{levelName}.json");
        string json = BuildEditorJson();

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(filePath, json);

        AssetDatabase.Refresh();
        LoadLevelList();

        int savedLevelIndex = FindLevelIndexByName(levelName);

        if (savedLevelIndex >= 0)
        {
            selectedEditorLevelIndex = savedLevelIndex;
            UpdateSelectedEditorLevelName();
        }

        loadedEditorLevelName = levelName;
        isEditingNewEditorLevel = false;

        Debug.Log($"Уровень сохранен в файл: {filePath}");
#else
        Debug.LogError("Save доступен только в Unity Editor.");
#endif
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

    private string GetEditorSaveLevelName()
    {
        if (!isEditingNewEditorLevel && !string.IsNullOrEmpty(loadedEditorLevelName))
        {
            return loadedEditorLevelName;
        }

        return $"level_{Mathf.Max(1, editorLevelId):000}";
    }

    private void OnGUI()
    {
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 36;
        labelStyle.normal.textColor = Color.white;

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 32;

        if (isLevelSelectMenuOpen)
        {
            DrawLevelSelectGui(labelStyle, buttonStyle);
            return;
        }

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

        if (GUI.Button(new Rect(Screen.width - 250, 30, 220, 70), "Levels", buttonStyle))
        {
            OpenLevelSelectMenu();
        }

        if (GUI.Button(new Rect(Screen.width - 250, 120, 220, 70), "Editor", buttonStyle))
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

    private void DrawLevelSelectGui(GUIStyle labelStyle, GUIStyle buttonStyle)
    {
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

        GUIStyle titleStyle = new GUIStyle(labelStyle);
        titleStyle.fontSize = 42;
        titleStyle.alignment = TextAnchor.MiddleCenter;

        GUIStyle levelButtonStyle = new GUIStyle(buttonStyle);
        levelButtonStyle.fontSize = 24;

        GUI.Label(new Rect(0, 40, Screen.width, 70), "LEVEL SELECT", titleStyle);

        if (levelAssets == null || levelAssets.Length == 0)
        {
            GUI.Label(new Rect(0, 140, Screen.width, 60), "No levels found", titleStyle);
        }
        else
        {
            float buttonWidth = 180f;
            float buttonHeight = 70f;
            float gap = 20f;
            float startX = 60f;
            float startY = 140f;
            int buttonsPerRow = Mathf.Max(1, Mathf.FloorToInt((Screen.width - startX * 2f + gap) / (buttonWidth + gap)));

            for (int i = 0; i < levelAssets.Length; i++)
            {
                int row = i / buttonsPerRow;
                int column = i % buttonsPerRow;

                float x = startX + column * (buttonWidth + gap);
                float y = startY + row * (buttonHeight + gap);

                string title = $"Level {i + 1}";

                if (i == currentLevelIndex)
                {
                    title += " *";
                }

                if (GUI.Button(new Rect(x, y, buttonWidth, buttonHeight), title, levelButtonStyle))
                {
                    LoadGameLevel(i);
                }
            }
        }

        float bottomY = Screen.height - 110f;

        if (GUI.Button(new Rect(Screen.width / 2f - 260f, bottomY, 240f, 70f), "Editor", buttonStyle))
        {
            isLevelSelectMenuOpen = false;
            EnterEditorMode();
        }

        if (GUI.Button(new Rect(Screen.width / 2f + 20f, bottomY, 240f, 70f), "Close", buttonStyle))
        {
            CloseLevelSelectMenu();
        }
    }

    private void DrawEditorGui(GUIStyle labelStyle, GUIStyle buttonStyle)
    {
        GUI.Box(new Rect(0, 0, Screen.width, EditorTopGuiHeight), "");

        GUIStyle editorLabelStyle = new GUIStyle(labelStyle);
        editorLabelStyle.fontSize = 24;

        GUIStyle smallButtonStyle = new GUIStyle(buttonStyle);
        smallButtonStyle.fontSize = 22;

        GUIStyle hintStyle = new GUIStyle(GUI.skin.label);
        hintStyle.fontSize = 18;
        hintStyle.normal.textColor = Color.white;

        GUI.Label(new Rect(30, 20, 500, 40), "EDITOR MODE", editorLabelStyle);

        GUI.Label(new Rect(30, 65, 280, 40), $"Selected: {selectedEditorLevelName}", editorLabelStyle);

        if (GUI.Button(new Rect(325, 65, 80, 42), "Prev", smallButtonStyle))
        {
            SelectPreviousEditorLevel();
        }

        if (GUI.Button(new Rect(415, 65, 80, 42), "Next", smallButtonStyle))
        {
            SelectNextEditorLevel();
        }

        if (GUI.Button(new Rect(505, 65, 80, 42), "Load", smallButtonStyle))
        {
            LoadSelectedEditorLevel();
        }

        if (GUI.Button(new Rect(595, 65, 120, 42), "New Level", smallButtonStyle))
        {
            CreateNewEditorLevel();
        }

        GUI.Label(new Rect(30, 115, 600, 40), $"Save target: {GetEditorSaveLevelName()}.json", editorLabelStyle);

        GUI.Label(new Rect(30, 165, 150, 40), $"Level ID: {editorLevelId}", editorLabelStyle);

        if (GUI.Button(new Rect(185, 165, 50, 42), "-", smallButtonStyle))
        {
            editorLevelId = Mathf.Max(1, editorLevelId - 1);
        }

        if (GUI.Button(new Rect(245, 165, 50, 42), "+", smallButtonStyle))
        {
            editorLevelId++;
        }

        GUI.Label(new Rect(325, 165, 120, 40), $"Rows: {editorRows}", editorLabelStyle);

        if (GUI.Button(new Rect(450, 165, 50, 42), "-", smallButtonStyle))
        {
            editorRows = Mathf.Max(3, editorRows - 1);
            RebuildEditorBoard();
        }

        if (GUI.Button(new Rect(510, 165, 50, 42), "+", smallButtonStyle))
        {
            editorRows++;
            RebuildEditorBoard();
        }

        GUI.Label(new Rect(30, 215, 160, 40), $"Columns: {editorColumns}", editorLabelStyle);

        if (GUI.Button(new Rect(200, 215, 50, 42), "-", smallButtonStyle))
        {
            editorColumns = Mathf.Max(3, editorColumns - 1);
            RebuildEditorBoard();
        }

        if (GUI.Button(new Rect(260, 215, 50, 42), "+", smallButtonStyle))
        {
            editorColumns++;
            RebuildEditorBoard();
        }

        GUI.Label(new Rect(325, 215, 130, 40), $"Lives: {editorLives}", editorLabelStyle);

        if (GUI.Button(new Rect(460, 215, 50, 42), "-", smallButtonStyle))
        {
            editorLives = Mathf.Max(1, editorLives - 1);
            lives = editorLives;
        }

        if (GUI.Button(new Rect(520, 215, 50, 42), "+", smallButtonStyle))
        {
            editorLives++;
            lives = editorLives;
        }

        if (GUI.Button(new Rect(30, 280, 120, 55), "Save", smallButtonStyle))
        {
            SaveEditorJsonToFile();
        }

        if (GUI.Button(new Rect(165, 280, 180, 55), "Export JSON", smallButtonStyle))
        {
            ExportEditorJson();
        }

        if (GUI.Button(new Rect(360, 280, 120, 55), "Clear", smallButtonStyle))
        {
            RebuildEditorBoard();
        }

        if (GUI.Button(new Rect(495, 280, 120, 55), "Back", smallButtonStyle))
        {
            ExitEditorMode();
        }

        GUI.Label(
            new Rect(30, 350, Screen.width - 60, 40),
            "Load opens a level. New Level creates empty level. Save writes JSON file. Export only copies JSON to clipboard.",
            hintStyle
        );
    }
}
