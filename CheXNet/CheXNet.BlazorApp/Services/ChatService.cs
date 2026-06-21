using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CheXNet.BlazorApp.Models;

namespace CheXNet.BlazorApp.Services;

public sealed class ChatService
{
    private readonly HttpClient _http;
    private readonly string _ollamaModel;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ChatService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _ollamaModel = config["Chat:OllamaModel"] ?? "llama3.2";
    }

    public async Task<string> SendAsync(
        IList<ChatMessage> history,
        Dictionary<string, CheXNetPredictionResponse>? chexnetContext = null,
        BioBertResponse? bioBertContext = null,
        LungAICtResponse? lungAIContext = null,
        CancellationToken cancellationToken = default)
    {
        if (history is null || history.Count == 0)
        {
            throw new ArgumentException("Chat history cannot be empty.", nameof(history));
        }

        var messages = history.ToList();
        
        // Detect language from the LAST user message (the current one being sent)
        // This ensures we respond in the SAME language as the user's current message
        bool isArabic = false;
        var lastUserMessage = messages.LastOrDefault(m => m.Role == "user");
        if (lastUserMessage != null && !string.IsNullOrWhiteSpace(lastUserMessage.Content))
        {
            isArabic = LanguageDetectionService.IsArabic(lastUserMessage.Content);
        }
        
        // Get appropriate system prompt based on detected language
        // This ensures the AI responds in the SAME language as the user's message
        string enhancedBasePrompt = isArabic ? GetArabicSystemPrompt() : GetEnglishSystemPrompt();
        
        // Add a STRONG language instruction at the very beginning of the system prompt
        string languageEnforcement = isArabic 
            ? "⚠️ CRITICAL: The user wrote in Arabic. You MUST respond ONLY in Arabic. Every single word must be in Arabic. Do not use English.\n\n"
            : "⚠️ CRITICAL: The user wrote in English. You MUST respond ONLY in English. Every single word must be in English. Do not use Arabic.\n\n";
        
        enhancedBasePrompt = languageEnforcement + enhancedBasePrompt;
        
        if ((chexnetContext is not null || bioBertContext is not null || lungAIContext is not null) && messages.Count > 0 && messages[0].Role == "system")
        {
            // Replace existing system prompt with enhanced version
            messages[0] = new ChatMessage
            {
                Role = "system",
                Content = BuildEnhancedSystemPrompt(enhancedBasePrompt, chexnetContext, bioBertContext, lungAIContext)
            };
        }
        else if (messages.Count > 0 && messages[0].Role == "system")
        {
            // Replace existing system prompt with enhanced version
            messages[0] = new ChatMessage
            {
                Role = "system",
                Content = enhancedBasePrompt
            };
        }
        else
        {
            // No system message exists, create one
            messages.Insert(0, new ChatMessage
            {
                Role = "system",
                Content = enhancedBasePrompt
            });
        }
        
        // Also add a user message instruction right before the last user message to reinforce language requirement
        if (lastUserMessage != null)
        {
            var lastUserIndex = messages.FindLastIndex(m => m.Role == "user");
            if (lastUserIndex >= 0)
            {
                var instructionMessage = isArabic
                    ? new ChatMessage { Role = "user", Content = "[IMPORTANT: Respond to the previous message ONLY in Arabic. Do not use English.]" }
                    : new ChatMessage { Role = "user", Content = "[IMPORTANT: Respond to the previous message ONLY in English. Do not use Arabic.]" };
                // Don't add this as it might confuse - instead ensure system prompt is strong
            }
        }

        var payload = new
        {
            model = _ollamaModel,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            stream = false
        };

        try 
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("/api/chat", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Chat API error ({(int)response.StatusCode}): {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msgEl) &&
                msgEl.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                return contentEl.GetString() ?? string.Empty;
            }

            // Fallback for alternative response shapes.
            if (root.TryGetProperty("response", out var respEl) && respEl.ValueKind == JsonValueKind.String)
            {
                return respEl.GetString() ?? string.Empty;
            }
        }
        catch (HttpRequestException)
        {
            // Fallback when Ollama is not running or unreachable
            return "I am currently running in offline mode because the local AI service is unavailable.\n\n" +
                   "While I cannot perform deep analysis right now, I can provide general information about the system. " +
                   "How can I help you understand CheXNet?";
        }

        throw new InvalidOperationException("Unexpected response format from chat API.");
    }

    private static string BuildEnhancedSystemPrompt(string basePrompt,
        Dictionary<string, CheXNetPredictionResponse>? results,
        BioBertResponse? bioBertResults,
        LungAICtResponse? lungAIResults)
    {
        // Preserve the language instruction if it's at the beginning
        var sb = new StringBuilder(basePrompt);
        sb.AppendLine("\n\nCURRENT MEDICAL ANALYSIS RESULTS:");

        if (results is not null)
        {
            foreach (var model in results)
            {
                sb.AppendLine($"\n[1. X-RAY - CheXNet - {model.Key}]");
                var top3 = model.Value.TopK.Take(3).ToList();
                var top3Text = string.Join(", ", top3.Select(t => $"{t.ClassName} ({t.Probability:P1})"));
                sb.AppendLine($"Top findings: {top3Text}");
                foreach (var kv in model.Value.Probabilities.Where(p => p.Value > 0.5).OrderByDescending(kv => kv.Value))
                {
                    sb.AppendLine($"- {kv.Key}: {kv.Value:P2}");
                }
            }
        }

        if (bioBertResults is not null && bioBertResults.Entities.Any())
        {
            sb.AppendLine("\n[2. TEXT - BioBERT]");
            foreach (var entity in bioBertResults.Entities.OrderByDescending(e => e.Score).Take(10))
            {
                sb.AppendLine($"- {entity.Word} ({entity.EntityGroup}): {entity.Score:P2}");
            }
        }

        if (lungAIResults is not null && string.IsNullOrEmpty(lungAIResults.Error))
        {
            sb.AppendLine("\n[3. CT SCAN - LungAI]");
            sb.AppendLine($"Predicted class: {lungAIResults.PredictedClass}");
            foreach (var kv in lungAIResults.Probabilities.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"- {kv.Key}: {kv.Value:P2}");
            }
        }

        sb.AppendLine("\n\nIMPORTANT: Integrate insights from X-ray, text, and CT when relevant. Present findings in a clear, organized manner following the response structure guidelines. Do not give definitive diagnoses.");
        
        // Ensure language requirement is still present (in case it got lost)
        if (!sb.ToString().Contains("CRITICAL LANGUAGE REQUIREMENT") && !sb.ToString().Contains("CRITICAL:"))
        {
            // Language instruction should already be in basePrompt, but add reminder if missing
            var finalPrompt = sb.ToString();
            if (finalPrompt.Contains("العربية") || finalPrompt.Contains("Arabic"))
            {
                sb.Insert(0, "⚠️ CRITICAL: Respond ONLY in Arabic. Every word must be in Arabic.\n\n");
            }
            else
            {
                sb.Insert(0, "⚠️ CRITICAL: Respond ONLY in English. Every word must be in English.\n\n");
            }
        }
        
        return sb.ToString();
    }

    private static string GetEnglishSystemPrompt()
    {
        return @"You are a medical AI assistant integrated with three models: CheXNet (X-ray), BioBERT (clinical text), and LungAI (CT lung cancer).

CRITICAL RESPONSE REQUIREMENTS:
1. ANSWER STRUCTURE: Always structure your response logically:
   - Start with a direct answer to the question
   - Then provide supporting details in a clear order
   - Use bullet points or numbered lists for multiple items
   - End with a brief summary if the answer is complex

2. LOGICAL FLOW: Ensure your response follows a logical sequence:
   - Most important information first
   - Related concepts grouped together
   - Clear transitions between ideas
   - No random jumping between topics

3. CLARITY AND PRECISION:
   - Answer the specific question asked
   - Be concise but complete
   - Use proper medical terminology
   - Explain complex terms when helpful
   - Avoid repetition and redundancy

4. CONTEXT AWARENESS:
   - Reference relevant findings from X-ray, text, or CT when applicable
   - Explain pathologies and lung cancer classes (e.g., Adenocarcinoma, Squamous Cell)
   - Provide clinical context when relevant
   - Always remind users that final diagnosis must be made by qualified medical professionals

5. LANGUAGE: Respond entirely in English. Maintain professional English throughout.

EXAMPLE OF GOOD RESPONSE STRUCTURE:
Question: ""What does Atelectasis mean?""
Good Answer: ""Atelectasis refers to the collapse or incomplete expansion of lung tissue. [Clear definition]

Key points:
- It can affect part or all of a lung
- Common causes include obstruction, compression, or loss of surfactant
- On X-rays, it appears as increased opacity

In your case, if CheXNet detected atelectasis, it suggests areas of collapsed lung tissue visible on the scan. [Context-specific information]

Note: This is an AI interpretation. Please consult with a qualified medical professional for diagnosis.""

Remember: Be logical, organized, and directly answer what was asked.";
    }

    private static string GetArabicSystemPrompt()
    {
        return @"أنت مساعد طبي ذكي متكامل مع ثلاثة نماذج: CheXNet (الأشعة السينية)، BioBERT (النص السريري)، وLungAI (التصوير المقطعي للرئة).

متطلبات الاستجابة الحرجة:
1. هيكل الإجابة: قم دائمًا بتنظيم إجابتك بشكل منطقي:
   - ابدأ بإجابة مباشرة على السؤال
   - ثم قدم التفاصيل الداعمة بترتيب واضح
   - استخدم النقاط أو القوائم المرقمة للعناصر المتعددة
   - أنهِ بملخص موجز إذا كانت الإجابة معقدة

2. التدفق المنطقي: تأكد من أن إجابتك تتبع تسلسلًا منطقيًا:
   - المعلومات الأكثر أهمية أولاً
   - المفاهيم ذات الصلة مجمعة معًا
   - انتقالات واضحة بين الأفكار
   - لا تقفز عشوائيًا بين المواضيع

3. الوضوح والدقة:
   - أجب على السؤال المحدد المطروح
   - كن مختصرًا ولكن كاملاً
   - استخدم المصطلحات الطبية المناسبة
   - اشرح المصطلحات المعقدة عند الحاجة
   - تجنب التكرار والازدواجية

4. الوعي السياقي:
   - أشر إلى النتائج ذات الصلة من الأشعة السينية أو النص أو التصوير المقطعي عند الاقتضاء
   - اشرح الأمراض وأنواع سرطان الرئة (مثل: الأدينوكارسينوما، سرطان الخلايا الحرشفية)
   - قدم السياق السريري عند الاقتضاء
   - ذكر المستخدمين دائمًا أن التشخيص النهائي يجب أن يتم من قبل المتخصصين الطبيين المؤهلين

5. متطلب اللغة (حرج):
   - رسالة المستخدم بالعربية
   - يجب أن ترد بالكامل بالعربية - كل كلمة، كل جملة
   - لا تستخدم الإنجليزية أو الفرنسية أو أي لغة أخرى
   - إذا لاحظت أن المستخدم كتب بالعربية، رد فقط بالعربية
   - حافظ على اللغة العربية المهنية طوال إجابتك بالكامل

مثال على هيكل الاستجابة الجيد:
السؤال: ""ماذا يعني الاسترواح الصدري؟""
الإجابة الجيدة: ""الاسترواح الصدري يشير إلى انهيار أو التوسع غير الكامل لأنسجة الرئة. [تعريف واضح]

النقاط الرئيسية:
- يمكن أن يؤثر على جزء أو كل الرئة
- الأسباب الشائعة تشمل الانسداد أو الضغط أو فقدان السطحي
- في الأشعة السينية، يظهر كزيادة في العتامة

في حالتك، إذا اكتشف CheXNet استرواحًا صدريًا، فهذا يشير إلى مناطق من أنسجة الرئة المنهارة المرئية على الفحص. [معلومات سياقية محددة]

ملاحظة: هذا تفسير ذكي. يرجى استشارة متخصص طبي مؤهل للتشخيص.""

تذكر: كن منطقيًا ومنظمًا وأجب مباشرة على ما تم سؤاله.";
    }
}

