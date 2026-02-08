using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;

namespace MedicalAIPlatform.Controllers
{
    [Authorize]
    public class AIAssistantController : Controller
    {
        private readonly ChatService _chatService;
        private readonly AnalyticsStateService _analyticsStateService;

        public AIAssistantController(ChatService chatService, AnalyticsStateService analyticsStateService)
        {
            _chatService = chatService;
            _analyticsStateService = analyticsStateService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "Message is required" });
            }

            try
            {
                // Get context from AnalyticsStateService
                var chexnetContext = _analyticsStateService.CurrentResults;
                var bioBertContext = _analyticsStateService.CurrentBioBertResults;
                var lungAIContext = _analyticsStateService.CurrentLungAIResults;

                // Build history from session or request
                var history = request.History ?? new List<ChatMessage>();
                
                // Add the new user message
                history.Add(new ChatMessage { Role = "user", Content = request.Message });

                // Send to chat service
                var response = await _chatService.SendAsync(history, chexnetContext, bioBertContext, lungAIContext);

                // Add assistant response to history
                history.Add(new ChatMessage { Role = "assistant", Content = response });

                return Ok(new { 
                    message = response,
                    history = history
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public List<ChatMessage>? History { get; set; }
    }
}
