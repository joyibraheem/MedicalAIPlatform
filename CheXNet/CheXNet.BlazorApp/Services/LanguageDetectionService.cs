namespace CheXNet.BlazorApp.Services;

public static class LanguageDetectionService
{
    /// <summary>
    /// Detects if the text contains Arabic characters.
    /// Returns true if Arabic is detected, false otherwise (defaults to English).
    /// </summary>
    public static bool IsArabic(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Arabic Unicode range: \u0600-\u06FF
        // This includes Arabic, Persian, Urdu, and other languages using Arabic script
        int arabicCharCount = 0;
        int totalCharCount = 0;

        foreach (char c in text)
        {
            // Skip whitespace and punctuation
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsDigit(c))
                continue;

            totalCharCount++;
            
            if (c >= '\u0600' && c <= '\u06FF')
            {
                arabicCharCount++;
            }
        }

        // If we have meaningful characters and at least 20% are Arabic, consider it Arabic
        // Lowered threshold to catch more Arabic messages
        if (totalCharCount > 0)
        {
            return (double)arabicCharCount / totalCharCount >= 0.2;
        }
        
        // If we found any Arabic characters at all and have at least 3 characters, consider it Arabic
        if (arabicCharCount > 0 && totalCharCount >= 3)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the language instruction for the system prompt based on detected language.
    /// Provides clear, strong instructions to ensure the AI responds in the correct language.
    /// </summary>
    public static string GetLanguageInstruction(string userMessage)
    {
        if (IsArabic(userMessage))
        {
            return "CRITICAL: The user wrote in Arabic. You MUST respond entirely in Arabic. Every word must be in Arabic. Do not use English or any other language.";
        }
        else
        {
            return "CRITICAL: The user wrote in English. You MUST respond entirely in English. Every word must be in English. Do not use Arabic or any other language.";
        }
    }
}
