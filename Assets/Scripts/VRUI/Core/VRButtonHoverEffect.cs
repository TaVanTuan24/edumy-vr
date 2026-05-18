using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Adds visual hover feedback to UGUI Buttons when the XR controller ray
/// hovers over them. Works with EventTrigger pointer enter/exit events
/// and also polls Button.interactable + EventSystem state as a fallback.
/// Attach this to any Button GameObject to get VR hover highlighting.
/// </summary>
public class VRButtonHoverEffect : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [Header("Colors")]
    [SerializeField] private Color hoverColor = new Color(0.30f, 0.65f, 1f, 1f);
    [SerializeField] private Color pressedColor = new Color(0.12f, 0.45f, 0.85f, 1f);
    [SerializeField] private float colorLerpSpeed = 12f;
    [SerializeField] private float hoverScaleMultiplier = 1.05f;

    private Button button;
    private Image image;
    private Color originalColor;
    private Color targetColor;
    private Vector3 originalScale;
    private Vector3 targetScale;
    private bool isHovered;
    private bool isPressed;

    private void Awake()
    {
        button = GetComponent<Button>();
        image = GetComponent<Image>();

        if (image != null)
        {
            originalColor = image.color;
            targetColor = originalColor;
        }

        originalScale = transform.localScale;
        targetScale = originalScale;
    }

    private void OnEnable()
    {
        // Reset state when re-enabled
        isHovered = false;
        isPressed = false;
        if (image != null)
        {
            originalColor = image.color;
            targetColor = originalColor;
            image.color = originalColor;
        }
        originalScale = transform.localScale;
        targetScale = originalScale;
        transform.localScale = originalScale;
    }

    private void Update()
    {
        // When button is not interactable (e.g. quiz answer locked), do NOT
        // override the color — let the quiz rendering logic control it (green/red).
        bool interactable = button == null || button.interactable;

        if (image != null && interactable)
        {
            image.color = Color.Lerp(image.color, targetColor, Time.unscaledDeltaTime * colorLerpSpeed);
        }

        if (interactable)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * colorLerpSpeed);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;

        isHovered = true;
        targetColor = hoverColor;
        targetScale = originalScale * hoverScaleMultiplier;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        isPressed = false;

        // Only reset color if the button is still interactable.
        // If not interactable (quiz answered), don't override the result colors (green/red).
        if (button == null || button.interactable)
        {
            targetColor = originalColor;
        }
        targetScale = originalScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && !button.interactable) return;

        isPressed = true;
        targetColor = pressedColor;
        targetScale = originalScale * 0.97f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        if (isHovered && (button == null || button.interactable))
        {
            targetColor = hoverColor;
            targetScale = originalScale * hoverScaleMultiplier;
        }
        else if (button == null || button.interactable)
        {
            targetColor = originalColor;
            targetScale = originalScale;
        }
    }

    /// <summary>
    /// Call this if the button's base color changes at runtime (e.g. pin toggle).
    /// </summary>
    public void SetOriginalColor(Color color)
    {
        originalColor = color;
        if (!isHovered && !isPressed)
        {
            targetColor = color;
        }
    }

    /// <summary>
    /// Returns the current original (non-hover) color.
    /// </summary>
    public Color OriginalColor => originalColor;
}