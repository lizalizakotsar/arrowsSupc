using System.Collections;
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

    private ArrowView[,] grid;
    private int arrowsLeft;
    private bool isBusy;
    private bool isGameOver;
    private bool isLevelCompleted;
    private bool isAllLevelsCompleted;

    private Sprite squareSprite;

    private TextAsset[] levelAssets; // загрузка левелов

    private void Start()
    {
        squareSprite = CreateSquareSprite();

        LoadLevelList();
        SetupCamera();
        GenerateLevel();
    }

    private void Update()
    {
        if (isBusy || isGameOver || isLevelCompleted)
        {
            return;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TrySelectArrow(Mouse.current.position.ReadValue());
        }

        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;

            if (touch.press.wasPressedThisFrame)
            {
                TrySelectArrow(touch.position.ReadValue());
            }
        }
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
        if (isBusy || isGameOver || isLevelCompleted)
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

    private void SetupCamera()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return;
        }

        mainCamera.orthographic = true;
        mainCamera.orthographicSize = rows * cellSize / 2f + 1.2f;
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

        GenerateLevel();

        Debug.Log($"Переход на уровень {currentLevelIndex + 1}.");
    }

    private void ClearBoard()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }
    }

    private void OnGUI()
    {
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 36;
        labelStyle.normal.textColor = Color.white;

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 32;

        GUI.Label(new Rect(30, 30, 400, 60), $"Level: {currentLevelIndex + 1}", labelStyle);
        GUI.Label(new Rect(30, 90, 400, 60), $"Lives: {lives}", labelStyle);
        GUI.Label(new Rect(30, 150, 400, 60), $"Arrows left: {arrowsLeft}", labelStyle);

        if (GUI.Button(new Rect(30, 220, 220, 70), "Restart", buttonStyle))
        {
            RestartLevel();
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
}