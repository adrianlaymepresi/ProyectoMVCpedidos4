// Controllers/HomeController.cs
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PracticaPedidos4MVC.Data;
using PracticaPedidos4MVC.Models;

namespace PracticaPedidos4MVC.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly PedidosDBContext _context;

        // Claves de sesión
        private const string SK_USER_ID = "Auth:UserId";
        private const string SK_USER_NAME = "Auth:UserName";
        private const string SK_USER_ROLE = "Auth:UserRole";
        private const string SK_FAILED_COUNT = "Auth:FailedCount";
        private const string SK_BLOCK_UNTIL = "Auth:BlockUntil";
        private static readonly TimeSpan BlockWindow = TimeSpan.FromMinutes(5);

        public HomeController(ILogger<HomeController> logger, PedidosDBContext context)
        {
            _logger = logger;
            _context = context;
        }

        // GET: pantalla de login (layout público)
        [HttpGet]
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32(SK_USER_ID);
            if (userId.HasValue)
            {
                // Redirección según rol
                var role = (HttpContext.Session.GetString(SK_USER_ROLE) ?? "cliente").ToLowerInvariant();
                return role switch
                {
                    "admin" => RedirectToAction("Index", "Users"),
                    "empleado" => RedirectToAction("Index", "Orders"),
                    _ => RedirectToAction("Index", "Catalog")
                };
            }

            ViewData["Title"] = "Iniciar sesión";
            return View(new LoginViewModel());
        }

        // POST: intento de login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(LoginViewModel vm)
        {
            try
            {
                // ¿Bloqueado?
                var blockTicks = HttpContext.Session.GetString(SK_BLOCK_UNTIL);
                if (!string.IsNullOrEmpty(blockTicks))
                {
                    var blockUntil = new DateTimeOffset(long.Parse(blockTicks), TimeSpan.Zero);
                    if (DateTimeOffset.UtcNow < blockUntil)
                    {
                        ModelState.AddModelError(string.Empty, $"Demasiados intentos fallidos. Intenta después de las {blockUntil.LocalDateTime:T}.");
                        return View(vm);
                    }
                    else
                    {
                        HttpContext.Session.Remove(SK_BLOCK_UNTIL);
                        HttpContext.Session.Remove(SK_FAILED_COUNT);
                    }
                }

                if (!ModelState.IsValid) return View(vm);

                string email = (vm.Email ?? "").Trim();
                string usuario = (vm.Nombre ?? "").Trim();
                string password = (vm.Password ?? "").Trim();

                if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(usuario))
                {
                    ModelState.AddModelError(string.Empty, "Debes ingresar Email o Usuario.");
                    return View(vm);
                }

                // Buscar por email o usuario (comparación simple)
                UserModel? user;
                try
                {
                    user = await _context.Users
                        .FirstOrDefaultAsync(u =>
                            (!string.IsNullOrEmpty(email) && u.Email == email) ||
                            (!string.IsNullOrEmpty(usuario) && u.Nombre == usuario));
                }
                catch (Exception exLookup)
                {
                    _logger.LogError(exLookup, "Error consultando el usuario durante el login.");
                    ModelState.AddModelError(string.Empty, "No se pudo validar las credenciales. Intenta nuevamente.");
                    return View(vm);
                }

                if (user == null || (user.Password ?? "") != password)
                {
                    int fails = HttpContext.Session.GetInt32(SK_FAILED_COUNT) ?? 0;
                    fails++;
                    HttpContext.Session.SetInt32(SK_FAILED_COUNT, fails);

                    if (fails >= 3)
                    {
                        var until = DateTimeOffset.UtcNow.Add(BlockWindow);
                        HttpContext.Session.SetString(SK_BLOCK_UNTIL, until.UtcTicks.ToString());
                        ModelState.AddModelError(string.Empty, $"Cuenta bloqueada por {BlockWindow.TotalMinutes:0} minutos por intentos fallidos.");
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, "Credenciales inválidas.");
                    }
                    return View(vm);
                }

                // Éxito → guardar sesión mínima
                HttpContext.Session.SetInt32(SK_USER_ID, user.Id);
                HttpContext.Session.SetString(SK_USER_NAME, user.Nombre ?? user.Email ?? "Usuario");
                HttpContext.Session.SetString(SK_USER_ROLE, user.Rol ?? "cliente");

                // limpiar contadores
                HttpContext.Session.Remove(SK_FAILED_COUNT);
                HttpContext.Session.Remove(SK_BLOCK_UNTIL);

                // Redirección según rol
                var role = (user.Rol ?? "cliente").ToLowerInvariant();
                return role switch
                {
                    "admin" => RedirectToAction("Index", "Users"),
                    "empleado" => RedirectToAction("Index", "Orders"),
                    _ => RedirectToAction("Index", "Catalog")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general en POST /Home/Index (login).");
                ModelState.AddModelError(string.Empty, "Ocurrió un error al iniciar sesión. Intenta nuevamente.");
                return View(vm);
            }
        }

        // POST: logout (con confirmación desde el layout)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            try
            {
                HttpContext.Session.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cerrar sesión.");
            }
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
