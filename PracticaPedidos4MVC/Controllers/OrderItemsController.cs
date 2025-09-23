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
        public async Task<IActionResult> Create([Bind("Id,IdPedido,IdProducto,Cantidad")] OrderItemModel model)
        {
            var pedido = await _context.Orders.AsNoTracking()
                .Include(p => p.Cliente)
                .FirstOrDefaultAsync(p => p.Id == model.IdPedido);
            if (pedido == null) ModelState.AddModelError(string.Empty, "Pedido no válido.");

            ProductModel? productoLectura = null;
            if (model.IdProducto <= 0)
                ModelState.AddModelError(nameof(OrderItemModel.IdProducto), "Debes seleccionar un producto.");
            else
            {
                productoLectura = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == model.IdProducto);
                if (productoLectura == null)
                    ModelState.AddModelError(nameof(OrderItemModel.IdProducto), "El producto seleccionado no existe.");
            }

            if (model.Cantidad < 1)
                ModelState.AddModelError(nameof(OrderItemModel.Cantidad), "La cantidad debe ser al menos 1.");

            if (productoLectura != null && model.Cantidad > productoLectura.Stock)
                ModelState.AddModelError(nameof(OrderItemModel.Cantidad), $"Stock insuficiente. Disponible: {productoLectura.Stock}.");

            if (!ModelState.IsValid)
            {
                model.Producto = productoLectura;
                if (productoLectura != null)
                    model.Subtotal = decimal.Round(productoLectura.Precio * model.Cantidad, 2, MidpointRounding.AwayFromZero);
                ViewBag.Pedido = pedido;
                return View(model);
            }

            await using var tx = await _context.Database.BeginTransactionAsync();

            var producto = await _context.Products.FirstOrDefaultAsync(p => p.Id == model.IdProducto);
            if (producto == null)
            {
                ModelState.AddModelError(nameof(OrderItemModel.IdProducto), "El producto seleccionado no existe.");
                ViewBag.Pedido = pedido;
                return View(model);
            }
            if (producto.Stock < model.Cantidad)
            {
                ModelState.AddModelError(nameof(OrderItemModel.Cantidad), $"Stock insuficiente. Disponible: {producto.Stock}.");
                ViewBag.Pedido = pedido;
                model.Producto = producto;
                return View(model);
            }

            producto.Stock -= model.Cantidad;
            model.Subtotal = decimal.Round(producto.Precio * model.Cantidad, 2, MidpointRounding.AwayFromZero);

            _context.Add(model);
            await _context.SaveChangesAsync();

            // ⬇️ Recalcular total del pedido
            await RecalcularTotalPedido(model.IdPedido);

            await tx.CommitAsync();
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
        public async Task<IActionResult> Edit(int id, [Bind("Id,IdPedido,IdProducto,Cantidad")] OrderItemModel model)
        {
            if (id != model.Id) return NotFound();

            var pedido = await _context.Orders.AsNoTracking()
                .Include(p => p.Cliente)
                .FirstOrDefaultAsync(p => p.Id == model.IdPedido);
            if (pedido == null) ModelState.AddModelError(string.Empty, "Pedido no válido.");

            var original = await _context.OrderItems.AsNoTracking().FirstOrDefaultAsync(oi => oi.Id == id);
            if (original == null) return NotFound();

            ProductModel? productoLectura = null;
            if (model.IdProducto <= 0)
                ModelState.AddModelError(nameof(OrderItemModel.IdProducto), "Debes seleccionar un producto.");
            else
            {
                productoLectura = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == model.IdProducto);
                if (productoLectura == null)
                    ModelState.AddModelError(nameof(OrderItemModel.IdProducto), "El producto seleccionado no existe.");
            }

            if (model.Cantidad < 1)
                ModelState.AddModelError(nameof(OrderItemModel.Cantidad), "La cantidad debe ser al menos 1.");

            if (productoLectura != null)
            {
                if (model.IdProducto == original.IdProducto)
                {
                    var diff = model.Cantidad - original.Cantidad;
                    if (diff > 0 && diff > productoLectura.Stock)
                        ModelState.AddModelError(nameof(OrderItemModel.Cantidad), $"Stock insuficiente. Disponible: {productoLectura.Stock}.");
                }
                else
                {
                    if (model.Cantidad > productoLectura.Stock)
                        ModelState.AddModelError(nameof(OrderItemModel.Cantidad), $"Stock insuficiente. Disponible: {productoLectura.Stock}.");
                }
            }

            if (!ModelState.IsValid)
            {
                model.Producto = productoLectura;
                if (productoLectura != null)
                    model.Subtotal = decimal.Round(productoLectura.Precio * model.Cantidad, 2, MidpointRounding.AwayFromZero);
                ViewBag.Pedido = pedido;
                return View(model);
            }

            await using var tx = await _context.Database.BeginTransactionAsync();

            if (model.IdProducto == original.IdProducto)
            {
                var prod = await _context.Products.FirstOrDefaultAsync(p => p.Id == model.IdProducto);
                if (prod == null) return NotFound();

                var diff = model.Cantidad - original.Cantidad;
                if (diff > 0 && prod.Stock < diff)
                {
                    ModelState.AddModelError(nameof(OrderItemModel.Cantidad), $"Stock insuficiente. Disponible: {prod.Stock}.");
                    ViewBag.Pedido = pedido;
                    model.Producto = prod;
                    return View(model);
                }
                prod.Stock -= diff; // si diff < 0, regresa stock
                model.Subtotal = decimal.Round(prod.Precio * model.Cantidad, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                var prodOld = await _context.Products.FirstOrDefaultAsync(p => p.Id == original.IdProducto);
                var prodNew = await _context.Products.FirstOrDefaultAsync(p => p.Id == model.IdProducto);
                if (prodOld == null || prodNew == null) return NotFound();

                prodOld.Stock += original.Cantidad;
                if (prodNew.Stock < model.Cantidad)
                {
                    ModelState.AddModelError(nameof(OrderItemModel.Cantidad), $"Stock insuficiente. Disponible: {prodNew.Stock}.");
                    ViewBag.Pedido = pedido;
                    model.Producto = prodNew;
                    return View(model);
                }
                prodNew.Stock -= model.Cantidad;
                model.Subtotal = decimal.Round(prodNew.Precio * model.Cantidad, 2, MidpointRounding.AwayFromZero);
            }

            _context.Update(model);
            await _context.SaveChangesAsync();

            // ⬇️ Recalcular total del pedido
            await RecalcularTotalPedido(model.IdPedido);

            await tx.CommitAsync();
            return RedirectToAction(nameof(Index), new { pedidoId = model.IdPedido });
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
            var det = await _context.OrderItems.FirstOrDefaultAsync(d => d.Id == id);
            if (det == null) return RedirectToAction("Index", "Orders");

            await using var tx = await _context.Database.BeginTransactionAsync();

            var prod = await _context.Products.FirstOrDefaultAsync(p => p.Id == det.IdProducto);
            if (prod != null) prod.Stock += det.Cantidad;

            int pedidoId = det.IdPedido;

            _context.OrderItems.Remove(det);
            await _context.SaveChangesAsync();

            // ⬇️ Recalcular total del pedido
            await RecalcularTotalPedido(pedidoId);

            await tx.CommitAsync();
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

        // ⬇️ NUEVO: recalcular total del pedido
        private async Task RecalcularTotalPedido(int pedidoId)
        {
            // sumo desde DB (items ya guardados), default 0 si no hay
            var total = await _context.OrderItems
                            .Where(i => i.IdPedido == pedidoId)
                            .SumAsync(i => (decimal?)i.Subtotal) ?? 0m;

            var pedido = await _context.Orders.FirstOrDefaultAsync(p => p.Id == pedidoId);
            if (pedido == null) return;

            pedido.Total = decimal.Round(total, 2, MidpointRounding.AwayFromZero);
            await _context.SaveChangesAsync();
        }
    }
}
