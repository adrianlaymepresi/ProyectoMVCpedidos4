// Controllers/OrdersController.cs
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PracticaPedidos4MVC.Data;
using PracticaPedidos4MVC.Models;
using System.Linq;

namespace PracticaPedidos4MVC.Controllers
{
    public class OrdersController : Controller
    {
        private readonly PedidosDBContext _context;

        // Catálogo de estados válidos
        private static readonly string[] EstadosPermitidos =
            new[] { "Pendiente", "Procesado", "Enviado", "Entregado" };

        private static bool EsEstadoValido(string? e) =>
            !string.IsNullOrWhiteSpace(e) && EstadosPermitidos.Contains(e);

        public OrdersController(PedidosDBContext context)
        {
            _context = context;
        }

        // LISTADO con búsqueda por email del cliente + paginación
        public async Task<IActionResult> Index(int pagina = 1, int cantidadRegistrosPorPagina = 5, string q = "")
        {
            if (cantidadRegistrosPorPagina < 1) cantidadRegistrosPorPagina = 5;
            if (cantidadRegistrosPorPagina > 99) cantidadRegistrosPorPagina = 99;
            if (pagina < 1) pagina = 1;

            var todos = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Cliente) // Necesario para mostrar nombre y email
                .ToListAsync();

            var termino = (q ?? "").Trim();
            var terminoNorm = NormalizarTexto(termino);

            IEnumerable<OrderModel> fuente;
            if (terminoNorm.Length == 0)
            {
                fuente = todos.OrderBy(o => o.Fecha).ThenBy(o => o.Id);
            }
            else
            {
                fuente = todos
                    .Select(o => new
                    {
                        O = o,
                        MailNorm = NormalizarTexto(o.Cliente?.Email ?? "")
                    })
                    .Where(x => x.MailNorm.Contains(terminoNorm))
                    .Select(x => new
                    {
                        x.O,
                        Relev = CalcularRelevancia(x.MailNorm, terminoNorm)
                    })
                    .OrderBy(x => x.Relev.empieza)
                    .ThenBy(x => x.Relev.indice)
                    .ThenBy(x => x.Relev.diferenciaLongitud)
                    .ThenBy(x => x.O.Id)
                    .Select(x => x.O);
            }

            var totalRegistros = fuente.Count();
            var totalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)cantidadRegistrosPorPagina));
            if (pagina > totalPaginas) pagina = totalPaginas;

            const int Window = 10;
            int winStart = ((pagina - 1) / Window) * Window + 1;
            if (winStart < 1) winStart = 1;
            int winEnd = Math.Min(winStart + Window - 1, totalPaginas);

            int omitir = (pagina - 1) * cantidadRegistrosPorPagina;
            var items = fuente.Skip(omitir).Take(cantidadRegistrosPorPagina).ToList();

            ViewBag.PaginaActual = pagina;
            ViewBag.CantidadRegistrosPorPagina = cantidadRegistrosPorPagina;
            ViewBag.TextoBusqueda = termino;
            ViewBag.CantidadTotalPaginas = totalPaginas;
            ViewBag.PageWindowStart = winStart;
            ViewBag.PageWindowEnd = winEnd;
            ViewBag.HasPrevPage = pagina > 1;
            ViewBag.HasNextPage = pagina < totalPaginas;

            return View(items);
        }

        // DETALLES
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Cliente)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (order == null) return NotFound();

            return View(order);
        }

        // CREAR (GET) — Estado predefinido y bloqueado en la vista
        public IActionResult Create()
        {
            var m = new OrderModel
            {
                Fecha = DateTime.Now,
                Estado = "Pendiente",
                Total = 0m
            };
            return View(m);
        }

        // CREAR (POST) — Forzar estado Pendiente y total 0
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("IdCliente,Fecha")] OrderModel orderModel)
        {
            if (!ModelState.IsValid) return View(orderModel);

            orderModel.Estado = "Pendiente"; // forzado
            orderModel.Total = 0m;           // el total lo calculan los ítems

            _context.Add(orderModel);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // EDITAR (GET) — Proveer lista de estados (si quieres usar ViewBag)
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Cliente)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            ViewBag.Estados = EstadosPermitidos;
            return View(order);
        }

        // EDITAR (POST) — Validar estado permitido, mantener Total intacto
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,IdCliente,Fecha,Estado")] OrderModel orderModel)
        {
            if (id != orderModel.Id) return NotFound();

            if (!EsEstadoValido(orderModel.Estado))
                ModelState.AddModelError(nameof(OrderModel.Estado), "Estado inválido.");

            if (!ModelState.IsValid)
            {
                ViewBag.Estados = EstadosPermitidos;
                return View(orderModel);
            }

            var existing = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (existing == null) return NotFound();

            existing.IdCliente = orderModel.IdCliente;
            existing.Fecha = orderModel.Fecha;
            existing.Estado = orderModel.Estado;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // ELIMINAR
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Cliente)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (order == null) return NotFound();
            return View(order);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null) _context.Orders.Remove(order);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // ====== AJAX: Buscar usuarios (por Nombre o Email) para el modal ======
        [HttpGet]
        public async Task<IActionResult> BuscarUsuarios(string q = "", int pagina = 1, int cantidadRegistrosPorPagina = 5)
        {
            if (cantidadRegistrosPorPagina < 1) cantidadRegistrosPorPagina = 5;
            if (cantidadRegistrosPorPagina > 99) cantidadRegistrosPorPagina = 99;
            if (pagina < 1) pagina = 1;

            var termino = (q ?? "").Trim();
            var terminoNorm = NormalizarTexto(termino);

            var todos = await _context.Users
                .AsNoTracking()
                .Select(u => new
                {
                    u.Id,
                    u.Nombre,
                    u.Email,
                    u.Rol
                })
                .ToListAsync();

            IEnumerable<dynamic> fuente;
            if (terminoNorm.Length == 0)
            {
                fuente = todos
                    .OrderBy(u => u.Nombre)
                    .ThenBy(u => u.Email)
                    .ThenBy(u => u.Id);
            }
            else
            {
                fuente = todos
                    .Select(u =>
                    {
                        var nomNorm = NormalizarTexto(u.Nombre ?? "");
                        var mailNorm = NormalizarTexto(u.Email ?? "");
                        var r1 = CalcularRelevancia(nomNorm, terminoNorm);
                        var r2 = CalcularRelevancia(mailNorm, terminoNorm);
                        var best = (r1.empieza < r2.empieza) ||
                                   (r1.empieza == r2.empieza && (r1.indice < r2.indice ||
                                   (r1.indice == r2.indice && r1.diferenciaLongitud <= r2.diferenciaLongitud)))
                                   ? r1 : r2;

                        return new { U = u, nomNorm, mailNorm, Relev = best };
                    })
                    .Where(x => x.nomNorm.Contains(terminoNorm) || x.mailNorm.Contains(terminoNorm))
                    .OrderBy(x => x.Relev.empieza)
                    .ThenBy(x => x.Relev.indice)
                    .ThenBy(x => x.Relev.diferenciaLongitud)
                    .ThenBy(x => x.U.Id)
                    .Select(x => x.U);
            }

            var totalRegistros = fuente.Count();
            var totalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)cantidadRegistrosPorPagina));
            if (pagina > totalPaginas) pagina = totalPaginas;

            const int Window = 10;
            int winStart = ((pagina - 1) / Window) * Window + 1;
            if (winStart < 1) winStart = 1;
            int winEnd = Math.Min(winStart + Window - 1, totalPaginas);

            int omitir = (pagina - 1) * cantidadRegistrosPorPagina;
            var items = fuente.Skip(omitir).Take(cantidadRegistrosPorPagina)
                .Select(u => new
                {
                    id = u.Id,
                    nombre = u.Nombre ?? "",
                    email = u.Email ?? "",
                    rol = u.Rol ?? ""
                })
                .ToList();

            return Json(new
            {
                pagina,
                cantidadRegistrosPorPagina,
                totalPaginas,
                pageWindowStart = winStart,
                pageWindowEnd = winEnd,
                hasPrev = pagina > 1,
                hasNext = pagina < totalPaginas,
                items
            });
        }

        // ====== helpers ======
        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
            var descomp = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descomp.Length);
            foreach (var c in descomp)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private static (int empieza, int indice, int diferenciaLongitud) CalcularRelevancia(string nombreNormalizado, string terminoNormalizado)
        {
            var empieza = nombreNormalizado.StartsWith(terminoNormalizado, StringComparison.Ordinal) ? 0 : 1;
            var indice = nombreNormalizado.IndexOf(terminoNormalizado, StringComparison.Ordinal);
            if (indice < 0) indice = int.MaxValue;
            var dif = Math.Abs(nombreNormalizado.Length - terminoNormalizado.Length);
            return (empieza, indice, dif);
        }
    }
}
