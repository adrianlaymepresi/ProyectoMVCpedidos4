// Controllers/CatalogController.cs
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PracticaPedidos4MVC.Data;
using PracticaPedidos4MVC.Models;

namespace PracticaPedidos4MVC.Controllers
{
    public class CatalogController : Controller
    {
        private readonly PedidosDBContext _context;
        private readonly ILogger<CatalogController> _logger;

        public CatalogController(PedidosDBContext context, ILogger<CatalogController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // LISTADO de productos SOLO-LECTURA con búsqueda + paginación
        public async Task<IActionResult> Index(int pagina = 1, int cantidadRegistrosPorPagina = 5, string q = "")
        {
            try
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
                        .Select(p => new { P = p, NomNorm = NormalizarTexto(p.Nombre ?? ""), DescNorm = NormalizarTexto(p.Descripcion ?? "") })
                        .Where(x => x.NomNorm.Contains(terminoNorm) || x.DescNorm.Contains(terminoNorm))
                        .Select(x =>
                        {
                            var r1 = CalcularRelevancia(x.NomNorm, terminoNorm);
                            var r2 = CalcularRelevancia(x.DescNorm, terminoNorm);
                            var best = (r1.empieza < r2.empieza) ||
                                       (r1.empieza == r2.empieza && (r1.indice < r2.indice ||
                                       (r1.indice == r2.indice && r1.diferenciaLongitud <= r2.diferenciaLongitud)))
                                       ? r1 : r2;
                            return new { x.P, Relev = best };
                        })
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el catálogo.");
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar el catálogo.");
                ViewBag.PaginaActual = 1;
                ViewBag.CantidadRegistrosPorPagina = 5;
                ViewBag.TextoBusqueda = "";
                ViewBag.CantidadTotalPaginas = 1;
                ViewBag.PageWindowStart = 1;
                ViewBag.PageWindowEnd = 1;
                ViewBag.HasPrevPage = false;
                ViewBag.HasNextPage = false;
                return View(Enumerable.Empty<ProductModel>());
            }
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
