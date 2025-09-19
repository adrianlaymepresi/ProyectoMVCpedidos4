// Controllers/OrderItemsController.cs
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PracticaPedidos4MVC.Data;
using PracticaPedidos4MVC.Models;

namespace PracticaPedidos4MVC.Controllers
{
    public class OrderItemsController : Controller
    {
        private readonly PedidosDBContext _context;

        public OrderItemsController(PedidosDBContext context)
        {
            _context = context;
        }

        // LISTADO por pedido + búsqueda por nombre de producto + paginación
        public async Task<IActionResult> Index(int pedidoId, int pagina = 1, int cantidadRegistrosPorPagina = 5, string q = "")
        {
            if (pedidoId < 1) return NotFound();
            if (cantidadRegistrosPorPagina < 1) cantidadRegistrosPorPagina = 5;
            if (cantidadRegistrosPorPagina > 99) cantidadRegistrosPorPagina = 99;
            if (pagina < 1) pagina = 1;

            var pedido = await _context.Orders
                .AsNoTracking()
                .Include(p => p.Cliente)
                .FirstOrDefaultAsync(p => p.Id == pedidoId);
            if (pedido == null) return NotFound();

            var termino = (q ?? "").Trim();
            var terminoNorm = NormalizarTexto(termino);

            var lista = await _context.OrderItems
                .AsNoTracking()
                .Where(d => d.IdPedido == pedidoId)
                .Include(d => d.Producto)
                .ToListAsync();

            IEnumerable<OrderItemModel> fuente;
            if (terminoNorm.Length == 0)
            {
                fuente = lista.OrderBy(d => d.Producto!.Nombre).ThenBy(d => d.Id);
            }
            else
            {
                fuente = lista
                    .Select(d => new { D = d, NomNorm = NormalizarTexto(d.Producto?.Nombre ?? "") })
                    .Where(x => x.NomNorm.Contains(terminoNorm))
                    .Select(x => new { x.D, Relev = CalcularRelevancia(x.NomNorm, terminoNorm) })
                    .OrderBy(x => x.Relev.empieza)
                    .ThenBy(x => x.Relev.indice)
                    .ThenBy(x => x.Relev.diferenciaLongitud)
                    .ThenBy(x => x.D.Id)
                    .Select(x => x.D);
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

            ViewBag.Pedido = pedido;
            ViewBag.PedidoId = pedidoId;

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

        // CREAR
        public async Task<IActionResult> Create(int idPedido)
        {
            if (idPedido < 1) return NotFound();

            var pedido = await _context.Orders
                .AsNoTracking()
                .Include(p => p.Cliente)
                .FirstOrDefaultAsync(p => p.Id == idPedido);
            if (pedido == null) return NotFound();

            var model = new OrderItemModel { IdPedido = idPedido, Cantidad = 1 };
            ViewBag.Pedido = pedido;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,IdPedido,IdProducto,Cantidad,Subtotal")] OrderItemModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Pedido = await _context.Orders.AsNoTracking().Include(p => p.Cliente).FirstOrDefaultAsync(p => p.Id == model.IdPedido);
                return View(model);
            }

            _context.Add(model);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { pedidoId = model.IdPedido });
        }

        // EDITAR
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var det = await _context.OrderItems
                .Include(d => d.Pedido).ThenInclude(p => p.Cliente)
                .Include(d => d.Producto)
                .FirstOrDefaultAsync(d => d.Id == id);
            if (det == null) return NotFound();

            ViewBag.Pedido = det.Pedido;
            return View(det);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,IdPedido,IdProducto,Cantidad,Subtotal")] OrderItemModel model)
        {
            if (id != model.Id) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Pedido = await _context.Orders.AsNoTracking().Include(p => p.Cliente).FirstOrDefaultAsync(p => p.Id == model.IdPedido);
                return View(model);
            }

            try
            {
                _context.Update(model);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { pedidoId = model.IdPedido });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.OrderItems.Any(e => e.Id == model.Id)) return NotFound();
                ModelState.AddModelError(string.Empty, "Otro usuario modificó este registro. Recarga la página.");
                ViewBag.Pedido = await _context.Orders.AsNoTracking().Include(p => p.Cliente).FirstOrDefaultAsync(p => p.Id == model.IdPedido);
                return View(model);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "No se pudo guardar los cambios. Intenta nuevamente.");
                ViewBag.Pedido = await _context.Orders.AsNoTracking().Include(p => p.Cliente).FirstOrDefaultAsync(p => p.Id == model.IdPedido);
                return View(model);
            }
        }

        // DETALLES
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var detalle = await _context.OrderItems
                .AsNoTracking()
                .Include(d => d.Pedido).ThenInclude(p => p.Cliente)
                .Include(d => d.Producto)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (detalle == null) return NotFound();
            return View(detalle);
        }

        // ELIMINAR
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var det = await _context.OrderItems
                .Include(d => d.Pedido).ThenInclude(p => p.Cliente)
                .Include(d => d.Producto)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (det == null) return NotFound();

            return View(det);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var det = await _context.OrderItems.FindAsync(id);
            if (det == null) return RedirectToAction("Index", "Orders");

            int pedidoId = det.IdPedido;
            _context.OrderItems.Remove(det);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { pedidoId });
        }

        // ====== AJAX: Buscar productos (por nombre) para el modal ======
        [HttpGet]
        public async Task<IActionResult> BuscarProductos(string q = "", int pagina = 1, int cantidadRegistrosPorPagina = 5)
        {
            if (cantidadRegistrosPorPagina < 1) cantidadRegistrosPorPagina = 5;
            if (cantidadRegistrosPorPagina > 99) cantidadRegistrosPorPagina = 99;
            if (pagina < 1) pagina = 1;

            var termino = (q ?? "").Trim();
            var terminoNorm = NormalizarTexto(termino);

            var todos = await _context.Products.AsNoTracking().ToListAsync();

            IEnumerable<ProductModel> fuente;
            if (terminoNorm.Length == 0)
            {
                fuente = todos.OrderBy(p => p.Nombre).ThenBy(p => p.Id);
            }
            else
            {
                fuente = todos
                    .Select(p => new { P = p, NomNorm = NormalizarTexto(p.Nombre ?? "") })
                    .Where(x => x.NomNorm.Contains(terminoNorm))
                    .Select(x => new { x.P, Relev = CalcularRelevancia(x.NomNorm, terminoNorm) })
                    .OrderBy(x => x.Relev.empieza)
                    .ThenBy(x => x.Relev.indice)
                    .ThenBy(x => x.Relev.diferenciaLongitud)
                    .ThenBy(x => x.P.Id)
                    .Select(x => x.P);
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
                .Select(p => new
                {
                    id = p.Id,
                    nombre = p.Nombre,
                    descripcion = p.Descripcion,
                    precio = p.Precio,
                    stock = p.Stock
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
