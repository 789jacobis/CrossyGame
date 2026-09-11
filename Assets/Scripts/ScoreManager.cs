using TMPro;
using UnityEngine;

public sealed class ScoreManager : MonoBehaviour
{
    private const string HighScoreKey = "HighScore";

    [SerializeField] private Transform player;
    [SerializeField] private TMP_Text scoreText;

    private float startingY;
    private int currentScore;
    private int highScore;
    private bool isNewHighScore;

    public int CurrentScore => currentScore;
    public int HighScore => highScore;
    public bool IsNewHighScore => isNewHighScore;

    private void Awake()
    {
        if (player == null || scoreText == null)
        {
            Debug.LogError("ScoreManager requires Player and Score Text references.", this);
            enabled = false;
            return;
        }

        startingY = player.position.y;
        highScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        UpdateScoreText();
    }

    private void Update()
    {
        int forwardDistance = Mathf.Max(
            0,
            Mathf.FloorToInt(player.position.y - startingY + 0.01f));

        if (forwardDistance <= currentScore)
        {
            return;
        }

        currentScore = forwardDistance;

        if (currentScore > highScore)
        {
            highScore = currentScore;
            isNewHighScore = true;
            PlayerPrefs.SetInt(HighScoreKey, highScore);
        }

        UpdateScoreText();
    }

    private void UpdateScoreText()
    {
        scoreText.text = $"Score: {currentScore}   Best: {highScore}";
    }

    public void SaveHighScore()
    {
        if (isNewHighScore)
        {
            PlayerPrefs.Save();
        }
    }

    public int GetScoreForWorldY(float worldY)
    {
        return Mathf.Max(0, Mathf.FloorToInt(worldY - startingY + 0.01f));
    }
}
