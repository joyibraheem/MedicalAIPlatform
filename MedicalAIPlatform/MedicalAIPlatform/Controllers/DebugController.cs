using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using MedicalAIPlatform.Models;
using System.Linq;
using System.Threading.Tasks;

namespace MedicalAIPlatform.Controllers
{
    public class DebugController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public DebugController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var info = new
            {
                IsAuthenticated = User.Identity.IsAuthenticated,
                Name = User.Identity.Name,
                AuthenticationType = User.Identity.AuthenticationType,
                Claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
                RolesInUserPrincipal = User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList()
            };

            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                var dbRoles = await _userManager.GetRolesAsync(user);
                return Json(new { UserPrincipal = info, DatabaseUser = new { user.Id, user.Email, DbRoles = dbRoles } });
            }

            return Json(new { UserPrincipal = info, DatabaseUser = "Null (Not found in DB)" });
        }
    }
}
