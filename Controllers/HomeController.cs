using Microsoft.AspNetCore.Mvc;

namespace DBChatbot.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
