// Controllers/UsersController.cs
using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PracticaPedidos4MVC.Data;
using PracticaPedidos4MVC.Models;

namespace PracticaPedidos4MVC.Controllers
{
    public class UsersController : Controller
    {
        private readonly PedidosDBContext _context;
        private readonly ILogger<UsersController> _logger;

        // Reutilizamos el cliente DNS (cache + timeout razonable)
        private static readonly LookupClient Dns = new LookupClient(new LookupClientOptions
        {
            UseCache = true,
            Retries = 1,
            Timeout = TimeSpan.FromSeconds(3)
        });

        // Roles permitidos
        private static readonly string[] AllowedRoles = new[] { "admin", "empleado", "cliente" };

        public UsersController(PedidosDBContext context, ILogger<UsersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ===== Helpers de sesión/rol (manual, sin [Authorize]) =====
        private int? CurrentUserId() => HttpContext.Session.GetInt32("Auth:UserId");
        private string CurrentRole() => (HttpContext.Session.GetString("Auth:UserRole") ?? "").ToLowerInvariant();
        private bool IsAdmin() => CurrentRole() == "admin";
        private IActionResult ForbidToCatalogIfNotAdmin()
            => IsAdmin() ? null! : RedirectToAction("Index", "Catalog");

        // =========================
        //  LISTADO con búsqueda + paginación
        // =========================
        public async Task<IActionResult> Index(int pagina = 1, int cantidadRegistrosPorPagina = 5, string q = "")
        {
            var guard = ForbidToCatalogIfNotAdmin(); if (guard is not null) return guard;

            try
            {
                if (cantidadRegistrosPorPagina < 1) cantidadRegistrosPorPagina = 5;
                if (cantidadRegistrosPorPagina > 99) cantidadRegistrosPorPagina = 99;
                if (pagina < 1) pagina = 1;

                var todos = await _context.Users.AsNoTracking().ToListAsync();

                var termino = (q ?? "").Trim();
                var terminoNorm = NormalizarTexto(termino);

                IEnumerable<UserModel> fuente;

                if (terminoNorm.Length == 0)
                {
                    fuente = todos
                        .OrderBy(u => u.Nombre)
                        .ThenBy(u => u.Id);
                }
                else
                {
                    var query = todos
                        .Select(u => new
                        {
                            U = u,
                            NomNorm = NormalizarTexto(u.Nombre ?? ""),
                            EmailNorm = (u.Email ?? "").Trim().ToLowerInvariant(),
                            RolNorm = NormalizarTexto(u.Rol ?? "")
                        })
                        .Where(x => x.NomNorm.Contains(terminoNorm)
                                 || x.EmailNorm.Contains(terminoNorm)
                                 || x.RolNorm.Contains(terminoNorm))
                        .Select(x => new
                        {
                            x.U,
                            RelNom = CalcularRelevancia(x.NomNorm, terminoNorm),
                            RelEmail = CalcularRelevancia(x.EmailNorm, terminoNorm),
                            RelRol = CalcularRelevancia(x.RolNorm, terminoNorm)
                        })
                        .OrderBy(x => x.RelNom.empieza)
                        .ThenBy(x => x.RelNom.indice)
                        .ThenBy(x => x.RelNom.diferenciaLongitud)
                        .ThenBy(x => x.U.Id);

                    fuente = query.Select(x => x.U);
                }

                var totalRegistros = fuente.Count();
                var cantidadTotalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)cantidadRegistrosPorPagina));
                if (pagina > cantidadTotalPaginas) pagina = cantidadTotalPaginas;

                const int WindowSize = 10;
                int pageWindowStart = ((pagina - 1) / WindowSize) * WindowSize + 1;
                if (pageWindowStart < 1) pageWindowStart = 1;
                int pageWindowEnd = Math.Min(pageWindowStart + WindowSize - 1, cantidadTotalPaginas);

                int omitir = (pagina - 1) * cantidadRegistrosPorPagina;

                var items = fuente.Skip(omitir).Take(cantidadRegistrosPorPagina).ToList();

                ViewBag.PaginaActual = pagina;
                ViewBag.CantidadRegistrosPorPagina = cantidadRegistrosPorPagina;
                ViewBag.TextoBusqueda = termino;
                ViewBag.CantidadTotalPaginas = cantidadTotalPaginas;
                ViewBag.PageWindowStart = pageWindowStart;
                ViewBag.PageWindowEnd = pageWindowEnd;
                ViewBag.HasPrevPage = pagina > 1;
                ViewBag.HasNextPage = pagina < cantidadTotalPaginas;

                return View(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar listado de Users.");
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar los usuarios.");
                ViewBag.PaginaActual = 1;
                ViewBag.CantidadRegistrosPorPagina = 5;
                ViewBag.TextoBusqueda = "";
                ViewBag.CantidadTotalPaginas = 1;
                ViewBag.PageWindowStart = 1;
                ViewBag.PageWindowEnd = 1;
                ViewBag.HasPrevPage = false;
                ViewBag.HasNextPage = false;
                return View(Enumerable.Empty<UserModel>());
            }
        }

        public async Task<IActionResult> Details(int? id)
        {
            var guard = ForbidToCatalogIfNotAdmin(); if (guard is not null) return guard;

            try
            {
                if (id == null) return NotFound();
                var userModel = await _context.Users.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
                if (userModel == null) return NotFound();
                return View(userModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar Details de User {Id}.", id);
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar el detalle.");
                return RedirectToAction(nameof(Index));
            }
        }

        public IActionResult Create()
        {
            var guard = ForbidToCatalogIfNotAdmin(); if (guard is not null) return guard;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Nombre,Email,Password,Rol")] UserModel userModel)
        {
            var guard = ForbidToCatalogIfNotAdmin(); if (guard is not null) return guard;

            ValidarNombre(userModel);
            ValidarEmail(userModel);
            await ValidarEmailDominioAsync(userModel);
            ValidarPassword(userModel);
            ValidarRol(userModel);
            await ValidarDuplicadosAsync(userModel);

            if (!ModelState.IsValid) return View(userModel);

            try
            {
                _context.Add(userModel);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creando User.");
                ModelState.AddModelError(string.Empty, "No se pudo crear el usuario. Intenta nuevamente.");
                return View(userModel);
            }
        }

        public async Task<IActionResult> Edit(int? id)
        {
            var guard = ForbidToCatalogIfNotAdmin(); if (guard is not null) return guard;

            try
            {
                if (id == null) return NotFound();
                var userModel = await _context.Users.FindAsync(id);
                if (userModel == null) return NotFound();
                return View(userModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar Edit de User {Id}.", id);
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar el formulario de edición.");
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Nombre,Email,Password,Rol")] UserModel userModel)
        {
            var guard = ForbidToCatalogIfNotAdmin(); if (guard is not null) return guard;

            if (id != userModel.Id) return NotFound();

            ValidarNombre(userModel);
            ValidarEmail(userModel);
            await ValidarEmailDominioAsync(userModel);
            ValidarPassword(userModel);
            ValidarRol(userModel);
            await ValidarDuplicadosAsync(userModel, excluirId: id);

            if (!ModelState.IsValid) return View(userModel);

            try
            {
                _context.Update(userModel);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException exC)
            {
                _logger.LogError(exC, "Concurrencia al editar User {Id}.", id);
                if (!UserModelExists(userModel.Id)) return NotFound();
                ModelState.AddModelError(string.Empty, "Otro usuario modificó este registro. Recarga la página.");
                return View(userModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general al editar User {Id}.", id);
                ModelState.AddModelError(string.Empty, "No se pudieron guardar los cambios. Intenta nuevamente.");
                return View(userModel);
            }
        }

        public async Task<IActionResult> Delete(int? id)
        {
            var guard = ForbidToCatalogIfNotAdmin(); if (guard is not null) return guard;

            try
            {
                if (id == null) return NotFound();

                var userModel = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (userModel == null) return NotFound();

                // Bloquear eliminación de sí mismo (señal a la vista)
                ViewBag.ForbiddenSelfDelete = CurrentUserId().HasValue && CurrentUserId()!.Value == userModel.Id;

                return View(userModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar Delete de User {Id}.", id);
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar la eliminación.");
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var guard = ForbidToCatalogIfNotAdmin(); if (guard is not null) return guard;

            // Re-chequeo server: tampoco permitir en POST
            if (CurrentUserId().HasValue && CurrentUserId()!.Value == id)
            {
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var userModel = await _context.Users.FindAsync(id);
                if (userModel != null)
                {
                    _context.Users.Remove(userModel);
                    await _context.SaveChangesAsync();
                }
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error eliminando User {Id}.", id);
                ModelState.AddModelError(string.Empty, "No se pudo eliminar el usuario. Intenta nuevamente.");
                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                return user is null ? RedirectToAction(nameof(Index)) : View("Delete", user);
            }
        }

        private bool UserModelExists(int id) => _context.Users.Any(e => e.Id == id);

        // =========================
        //  Validaciones (ajustada la de Rol)
        // =========================
        private void ValidarNombre(UserModel u)
        {
            var nombre = (u.Nombre ?? "").Trim();
            if (string.IsNullOrEmpty(nombre))
                ModelState.AddModelError(nameof(UserModel.Nombre), "El nombre es obligatorio.");
            else if (nombre.Length < 5)
                ModelState.AddModelError(nameof(UserModel.Nombre), "Debe tener al menos 5 caracteres.");
            else if (nombre.Length > 120)
                ModelState.AddModelError(nameof(UserModel.Nombre), "Máximo 120 caracteres.");
            u.Nombre = nombre;
        }

        private void ValidarEmail(UserModel u)
        {
            var email = (u.Email ?? "").Trim();

            if (string.IsNullOrEmpty(email))
            {
                ModelState.AddModelError(nameof(UserModel.Email), "El email es obligatorio.");
                return;
            }
            if (email.Length < 7)
                ModelState.AddModelError(nameof(UserModel.Email), "Debe tener al menos 7 caracteres.");
            else if (email.Length > 320)
                ModelState.AddModelError(nameof(UserModel.Email), "Máximo 320 caracteres.");
            else
            {
                try { _ = new MailAddress(email); }
                catch { ModelState.AddModelError(nameof(UserModel.Email), "El formato de correo no es válido."); }
            }

            u.Email = email;
        }

        private async Task ValidarEmailDominioAsync(UserModel u)
        {
            if (ModelState.TryGetValue(nameof(UserModel.Email), out var entry) && entry.Errors.Count > 0)
                return;

            var domain = TryGetDomainFromEmail(u.Email ?? "");
            if (domain == null)
            {
                ModelState.AddModelError(nameof(UserModel.Email), "El formato de correo no es válido.");
                return;
            }

            var ok = await DominioTieneMxAsync(domain);
            if (!ok)
                ModelState.AddModelError(nameof(UserModel.Email), "El dominio del correo no existe o no tiene registros MX.");
        }

        private void ValidarPassword(UserModel u)
        {
            var pass = (u.Password ?? "").Trim();
            if (string.IsNullOrEmpty(pass))
                ModelState.AddModelError(nameof(UserModel.Password), "La contraseña es obligatoria.");
            else if (pass.Length < 8)
                ModelState.AddModelError(nameof(UserModel.Password), "Debe tener al menos 8 caracteres.");
            else if (pass.Length > 64)
                ModelState.AddModelError(nameof(UserModel.Password), "Máximo 64 caracteres.");
            u.Password = pass;
        }

        private void ValidarRol(UserModel u)
        {
            var rol = (u.Rol ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(rol))
            {
                ModelState.AddModelError(nameof(UserModel.Rol), "El rol es obligatorio.");
            }
            else if (!AllowedRoles.Contains(rol))
            {
                ModelState.AddModelError(nameof(UserModel.Rol), "Rol inválido. Debe ser: admin, empleado o cliente.");
            }
            u.Rol = rol; // normalizamos en minúsculas
        }

        private async Task ValidarDuplicadosAsync(UserModel u, int? excluirId = null)
        {
            var nombreParam = (u.Nombre ?? "").Trim();
            var emailParam = (u.Email ?? "").Trim().ToLowerInvariant();

            bool nombreDuplicado = await _context.Users.AsNoTracking()
                .Where(x => excluirId == null || x.Id != excluirId.Value)
                .AnyAsync(x => EF.Functions.Collate(x.Nombre, "SQL_Latin1_General_CP1_CI_AI") == nombreParam);

            if (nombreDuplicado)
                ModelState.AddModelError(nameof(UserModel.Nombre), "Ya existe un usuario con este nombre.");

            bool emailDuplicado = await _context.Users.AsNoTracking()
                .Where(x => excluirId == null || x.Id != excluirId.Value)
                .AnyAsync(x => ((x.Email ?? "").Trim().ToLower()) == emailParam);

            if (emailDuplicado)
                ModelState.AddModelError(nameof(UserModel.Email), "Ya existe un usuario con este email.");
        }

        // =========================
        //  Utilitarios
        // =========================
        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

            var descompuesto = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (var c in descompuesto)
            {
                var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private static string? TryGetDomainFromEmail(string email)
        {
            try
            {
                var addr = new MailAddress(email);
                var host = addr.Host?.Trim().TrimEnd('.');
                if (string.IsNullOrEmpty(host)) return null;

                var idn = new IdnMapping();
                return idn.GetAscii(host).ToLowerInvariant();
            }
            catch { return null; }
        }

        private static async Task<bool> DominioTieneMxAsync(string domain)
        {
            try
            {
                var resp = await Dns.QueryAsync(domain, QueryType.MX);
                var mx = resp.AllRecords.OfType<MxRecord>()
                                        .Where(r => !string.IsNullOrWhiteSpace(r.Exchange?.Value));
                return mx.Any();
            }
            catch
            {
                return false;
            }
        }

        private static (int empieza, int indice, int diferenciaLongitud) CalcularRelevancia(string baseNorm, string terminoNorm)
        {
            var empieza = baseNorm.StartsWith(terminoNorm, StringComparison.Ordinal) ? 0 : 1;
            var indice = baseNorm.IndexOf(terminoNorm, StringComparison.Ordinal);
            if (indice < 0) indice = int.MaxValue;
            var diferenciaLongitud = Math.Abs(baseNorm.Length - terminoNorm.Length);
            return (empieza, indice, diferenciaLongitud);
        }
    }
}
