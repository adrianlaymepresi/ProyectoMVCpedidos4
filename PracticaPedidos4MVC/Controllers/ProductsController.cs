using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PracticaPedidos4MVC.Data;
using PracticaPedidos4MVC.Models;

namespace PracticaPedidos4MVC.Controllers
{
    public class ProductsController : Controller
    {
        private readonly PedidosDBContext _context;

        public ProductsController(PedidosDBContext context)
        {
            _context = context;
        }

        // =========================
        //  LISTADO con búsqueda + paginación + filtro (nombre/categoría/precio)
        // =========================
        // GET: Products
        public async Task<IActionResult> Index(
            int pagina = 1,
            int cantidadRegistrosPorPagina = 5,
            string q = "",
            string modo = "nombre",          // "nombre" | "categoria" | "precio"
            decimal? minPrecio = null,
            decimal? maxPrecio = null)
        {
            try
            {
                // Normalización parámetros
                modo = (modo ?? "nombre").Trim().ToLowerInvariant();
                if (modo != "nombre" && modo != "categoria" && modo != "precio") modo = "nombre";

                if (cantidadRegistrosPorPagina < 1) cantidadRegistrosPorPagina = 5;
                if (cantidadRegistrosPorPagina > 99) cantidadRegistrosPorPagina = 99;
                if (pagina < 1) pagina = 1;

                var todos = await _context.Products
                    .AsNoTracking()
                    .ToListAsync();

                // VALIDACIONES de rango de precio cuando corresponde
                bool rangoValido = true;
                if (modo == "precio")
                {
                    if (minPrecio is null || maxPrecio is null)
                    {
                        rangoValido = false;
                        ModelState.AddModelError(string.Empty, "Debes ingresar ambos valores de precio (mínimo y máximo).");
                    }
                    else
                    {
                        if (minPrecio < 0.01m || minPrecio > 999_999.99m ||
                            maxPrecio < 0.01m || maxPrecio > 999_999.99m)
                        {
                            rangoValido = false;
                            ModelState.AddModelError(string.Empty, "Los precios deben estar entre 0.01 y 999,999.99.");
                        }
                        if (rangoValido && minPrecio > maxPrecio)
                        {
                            rangoValido = false;
                            ModelState.AddModelError(string.Empty, "El precio mínimo no puede ser mayor que el máximo.");
                        }
                    }
                }

                var termino = (q ?? "").Trim();
                var terminoNorm = NormalizarTexto(termino);

                IEnumerable<ProductModel> fuente;

                if (modo == "nombre")
                {
                    if (terminoNorm.Length == 0)
                    {
                        fuente = todos
                            .OrderBy(p => p.Nombre)
                            .ThenBy(p => p.Id);
                    }
                    else
                    {
                        var query = todos
                            .Select(p => new
                            {
                                P = p,
                                NomNorm = NormalizarTexto(p.Nombre ?? ""),
                                DescNorm = NormalizarTexto(p.Descripcion ?? "")
                            })
                            .Where(x => x.NomNorm.Contains(terminoNorm) || x.DescNorm.Contains(terminoNorm))
                            .Select(x => new
                            {
                                x.P,
                                RelNom = CalcularRelevancia(x.NomNorm, terminoNorm),
                                RelDesc = CalcularRelevancia(x.DescNorm, terminoNorm)
                            })
                            .OrderBy(x => x.RelNom.empieza)
                            .ThenBy(x => x.RelNom.indice)
                            .ThenBy(x => x.RelNom.diferenciaLongitud)
                            .ThenBy(x => x.P.Id);

                        fuente = query.Select(x => x.P);
                    }
                }
                else if (modo == "categoria")
                {
                    if (terminoNorm.Length == 0)
                    {
                        // Si no hay término, solo ordenamos por categoría/nombre
                        fuente = todos
                            .OrderBy(p => p.Categoria)
                            .ThenBy(p => p.Nombre)
                            .ThenBy(p => p.Id);
                    }
                    else
                    {
                        var query = todos
                            .Select(p => new
                            {
                                P = p,
                                CatNorm = NormalizarTexto(p.Categoria ?? "")
                            })
                            .Where(x => x.CatNorm.Contains(terminoNorm))
                            .Select(x => new
                            {
                                x.P,
                                Rel = CalcularRelevancia(x.CatNorm, terminoNorm)
                            })
                            .OrderBy(x => x.Rel.empieza)
                            .ThenBy(x => x.Rel.indice)
                            .ThenBy(x => x.Rel.diferenciaLongitud)
                            .ThenBy(x => x.P.Id);

                        fuente = query.Select(x => x.P);
                    }
                }
                else // modo == "precio"
                {
                    if (!rangoValido)
                    {
                        // Si el rango no es válido, mostramos todo ordenado por precio y dejamos los errores en ModelState
                        fuente = todos.OrderBy(p => p.Precio).ThenBy(p => p.Id);
                    }
                    else
                    {
                        // Rango válido => filtramos
                        var min = minPrecio!.Value;
                        var max = maxPrecio!.Value;

                        fuente = todos
                            .Where(p => p.Precio >= min && p.Precio <= max)
                            .OrderBy(p => p.Precio)
                            .ThenBy(p => p.Nombre)
                            .ThenBy(p => p.Id);
                    }
                }

                var totalRegistros = fuente.Count();
                var cantidadTotalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)cantidadRegistrosPorPagina));
                if (pagina > cantidadTotalPaginas) pagina = cantidadTotalPaginas;

                const int WindowSize = 10;
                int pageWindowStart = ((pagina - 1) / WindowSize) * WindowSize + 1;
                if (pageWindowStart < 1) pageWindowStart = 1;
                int pageWindowEnd = Math.Min(pageWindowStart + WindowSize - 1, cantidadTotalPaginas);

                int omitir = (pagina - 1) * cantidadRegistrosPorPagina;

                var items = fuente
                    .Skip(omitir)
                    .Take(cantidadRegistrosPorPagina)
                    .ToList();

                // ViewBags para la vista
                ViewBag.PaginaActual = pagina;
                ViewBag.CantidadRegistrosPorPagina = cantidadRegistrosPorPagina;
                ViewBag.TextoBusqueda = termino;
                ViewBag.Modo = modo;
                ViewBag.MinPrecio = minPrecio;
                ViewBag.MaxPrecio = maxPrecio;

                ViewBag.CantidadTotalPaginas = cantidadTotalPaginas;
                ViewBag.PageWindowStart = pageWindowStart;
                ViewBag.PageWindowEnd = pageWindowEnd;
                ViewBag.HasPrevPage = pagina > 1;
                ViewBag.HasNextPage = pagina < cantidadTotalPaginas;

                return View(items);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar los productos.");
                ViewBag.PaginaActual = 1;
                ViewBag.CantidadRegistrosPorPagina = 5;
                ViewBag.TextoBusqueda = "";
                ViewBag.Modo = "nombre";
                ViewBag.MinPrecio = null;
                ViewBag.MaxPrecio = null;
                ViewBag.CantidadTotalPaginas = 1;
                ViewBag.PageWindowStart = 1;
                ViewBag.PageWindowEnd = 1;
                ViewBag.HasPrevPage = false;
                ViewBag.HasNextPage = false;
                return View(Enumerable.Empty<ProductModel>());
            }
        }

        // GET: Products/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            try
            {
                var productModel = await _context.Products.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
                if (productModel == null) return NotFound();
                return View(productModel);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar el detalle.");
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: Products/Create
        public IActionResult Create() => View();

        // POST: Products/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Nombre,Descripcion,Categoria,Precio,Stock")] ProductModel productModel)
        {
            ValidarNombre(productModel);
            ValidarDescripcion(productModel);
            ValidarCategoria(productModel);
            ValidarPrecio(productModel);
            ValidarStock(productModel);
            await ValidarNombreUnicoAsync(productModel);

            if (!ModelState.IsValid) return View(productModel);

            try
            {
                _context.Add(productModel);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "No se pudo guardar el producto. Intenta nuevamente.");
                return View(productModel);
            }
        }

        // GET: Products/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            try
            {
                var productModel = await _context.Products.FindAsync(id);
                if (productModel == null) return NotFound();
                return View(productModel);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar el formulario de edición.");
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Products/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Nombre,Descripcion,Categoria,Precio,Stock")] ProductModel productModel)
        {
            if (id != productModel.Id) return NotFound();

            ValidarNombre(productModel);
            ValidarDescripcion(productModel);
            ValidarCategoria(productModel);
            ValidarPrecio(productModel);
            ValidarStock(productModel);
            await ValidarNombreUnicoAsync(productModel, excluirId: id);

            if (!ModelState.IsValid) return View(productModel);

            try
            {
                _context.Update(productModel);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductModelExists(productModel.Id)) return NotFound();
                ModelState.AddModelError(string.Empty, "Otro usuario modificó este registro. Recarga la página.");
                return View(productModel);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "No se pudieron guardar los cambios. Intenta nuevamente.");
                return View(productModel);
            }
        }

        // GET: Products/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            try
            {
                var productModel = await _context.Products.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
                if (productModel == null) return NotFound();
                return View(productModel);
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar la eliminación.");
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Products/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var productModel = await _context.Products.FindAsync(id);
                if (productModel != null)
                {
                    _context.Products.Remove(productModel);
                    await _context.SaveChangesAsync();
                }
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "No se pudo eliminar el producto. Intenta nuevamente.");
                var prod = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
                return prod is null ? RedirectToAction(nameof(Index)) : View("Delete", prod);
            }
        }

        private bool ProductModelExists(int id) => _context.Products.Any(e => e.Id == id);

        // =========================
        //  Validaciones en controlador
        // =========================
        private void ValidarNombre(ProductModel p)
        {
            var nombre = (p.Nombre ?? "").Trim();
            if (string.IsNullOrEmpty(nombre))
                ModelState.AddModelError(nameof(ProductModel.Nombre), "El nombre es obligatorio.");
            else if (nombre.Length < 4)
                ModelState.AddModelError(nameof(ProductModel.Nombre), "Debe tener al menos 4 caracteres.");
            else if (nombre.Length > 120)
                ModelState.AddModelError(nameof(ProductModel.Nombre), "Máximo 120 caracteres.");
            p.Nombre = nombre;
        }

        private void ValidarDescripcion(ProductModel p)
        {
            var desc = (p.Descripcion ?? "").Trim();
            if (desc.Length > 0 && desc.Length < 4)
                ModelState.AddModelError(nameof(ProductModel.Descripcion), "Si escribes descripción, debe tener al menos 4 caracteres.");
            else if (desc.Length > 1000)
                ModelState.AddModelError(nameof(ProductModel.Descripcion), "Máximo 1000 caracteres.");
            p.Descripcion = desc;
        }

        private void ValidarCategoria(ProductModel p)
        {
            var cat = (p.Categoria ?? "").Trim();
            if (cat.Length > 0 && cat.Length < 3)
                ModelState.AddModelError(nameof(ProductModel.Categoria), "Si escribes categoría, debe tener al menos 3 caracteres.");
            else if (cat.Length > 60)
                ModelState.AddModelError(nameof(ProductModel.Categoria), "Máximo 60 caracteres.");
            p.Categoria = string.IsNullOrWhiteSpace(cat) ? null : cat;
        }

        private void ValidarPrecio(ProductModel p)
        {
            var precio = p.Precio;

            if (precio < 0.01m || precio > 999_999.99m)
            {
                ModelState.AddModelError(nameof(ProductModel.Precio), "El precio debe estar entre 0.01 y 999,999.99.");
                return;
            }

            int decimales = (decimal.GetBits(precio)[3] >> 16) & 0x7F;
            if (decimales > 2)
                ModelState.AddModelError(nameof(ProductModel.Precio), "Máximo 2 decimales.");
        }

        private void ValidarStock(ProductModel p)
        {
            if (p.Stock < 0 || p.Stock > 100_000)
                ModelState.AddModelError(nameof(ProductModel.Stock), "El stock debe estar entre 0 y 100,000.");
        }

        private async Task ValidarNombreUnicoAsync(ProductModel p, int? excluirId = null)
        {
            var nombreParam = (p.Nombre ?? "").Trim().ToLowerInvariant();

            bool existe = await _context.Products
                .AsNoTracking()
                .Where(x => excluirId == null || x.Id != excluirId.Value)
                .AnyAsync(x => ((x.Nombre ?? "").Trim().ToLower()) == nombreParam);

            if (existe)
                ModelState.AddModelError(nameof(ProductModel.Nombre), "Ya existe un producto con el mismo nombre.");
        }

        // =========================
        //  Utilitarios de búsqueda
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
