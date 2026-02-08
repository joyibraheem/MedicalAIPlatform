namespace MedicalAIPlatform.Services;

public static class LanguageDetectionService
{
    public static bool IsArabic(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Check for Arabic Unicode range (U+0600 to U+06FF)
        int arabicCount = 0;
        int totalChars = 0;

        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                totalChars++;
                if (c >= 0x0600 && c <= 0x06FF)
                {
                    arabicCount++;
                }
            }
        }

        // If more than 30% of letters are Arabic, consider it Arabic
        return totalChars > 0 && (double)arabicCount / totalChars > 0.3;
    }
}
