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
        private readonly PedidosDBContext _dbContext;

        // Longitud máxima permitida para el texto de búsqueda del catálogo
        private const int LongitudMaximaBusqueda = 60;

        public CatalogController(PedidosDBContext dbContext)
        {
            _dbContext = dbContext;
        }

        // LISTADO de productos SOLO-LECTURA con búsqueda + paginación
        public async Task<IActionResult> Index(int pagina = 1, int cantidadRegistrosPorPagina = 5, string q = "")
        {
            try
            {
                // Normalización de parámetros de paginación
                int paginaNormalizada = pagina < 1 ? 1 : pagina;
                int registrosPorPaginaNormalizados = Math.Clamp(cantidadRegistrosPorPagina, 1, 99);

                // Sanitización del texto de búsqueda (defensa adicional contra payloads maliciosos)
                (string textoBusquedaSanitizado, string? mensajeBloqueoSeguridad) = SanearTextoBusqueda(q);
                ViewBag.TextoBusqueda = textoBusquedaSanitizado; // lo que queda tras sanitizar

                // Si se detectó una firma peligrosa, se ignora el filtro y se muestra un aviso
                if (!string.IsNullOrEmpty(mensajeBloqueoSeguridad))
                {
                    ViewBag.MensajeFiltroBloqueado = mensajeBloqueoSeguridad;
                    textoBusquedaSanitizado = string.Empty; // fuerza listado completo seguro
                }

                string terminoBusquedaNormalizado = NormalizarTexto(textoBusquedaSanitizado);

                // Cargar todos los productos en modo solo-lectura
                List<ProductModel> listaProductos = await _dbContext
                    .Products
                    .AsNoTracking()
                    .ToListAsync();

                IEnumerable<ProductModel> productosFiltrados;

                if (string.IsNullOrEmpty(terminoBusquedaNormalizado))
                {
                    productosFiltrados = listaProductos
                        .OrderBy(p => p.Nombre)
                        .ThenBy(p => p.Id);
                }
                else
                {
                    // Búsqueda en memoria (nombre/descripcion) con cálculo simple de relevancia
                    productosFiltrados = listaProductos
                        .Select(p => new
                        {
                            Producto = p,
                            NombreNormalizado = NormalizarTexto(p.Nombre ?? string.Empty),
                            DescripcionNormalizada = NormalizarTexto(p.Descripcion ?? string.Empty)
                        })
                        .Where(x => x.NombreNormalizado.Contains(terminoBusquedaNormalizado)
                                 || x.DescripcionNormalizada.Contains(terminoBusquedaNormalizado))
                        .Select(x =>
                        {
                            var relevanciaNombre = CalcularRelevancia(x.NombreNormalizado, terminoBusquedaNormalizado);
                            var relevanciaDescripcion = CalcularRelevancia(x.DescripcionNormalizada, terminoBusquedaNormalizado);

                            // Elegir la mejor relevancia entre nombre y descripción
                            var relevanciaGanadora =
                                (relevanciaNombre.empieza < relevanciaDescripcion.empieza) ||
                                (relevanciaNombre.empieza == relevanciaDescripcion.empieza &&
                                 (relevanciaNombre.indice < relevanciaDescripcion.indice ||
                                  (relevanciaNombre.indice == relevanciaDescripcion.indice &&
                                   relevanciaNombre.diferenciaLongitud <= relevanciaDescripcion.diferenciaLongitud)))
                                ? relevanciaNombre
                                : relevanciaDescripcion;

                            return new { x.Producto, Relevancia = relevanciaGanadora };
                        })
                        .OrderBy(x => x.Relevancia.empieza)
                        .ThenBy(x => x.Relevancia.indice)
                        .ThenBy(x => x.Relevancia.diferenciaLongitud)
                        .ThenBy(x => x.Producto.Id)
                        .Select(x => x.Producto);
                }

                // Paginación
                int totalRegistros = productosFiltrados.Count();
                int totalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)registrosPorPaginaNormalizados));
                if (paginaNormalizada > totalPaginas) paginaNormalizada = totalPaginas;

                const int TamanoVentanaPaginacion = 10;
                int inicioVentana = ((paginaNormalizada - 1) / TamanoVentanaPaginacion) * TamanoVentanaPaginacion + 1;
                if (inicioVentana < 1) inicioVentana = 1;
                int finVentana = Math.Min(inicioVentana + TamanoVentanaPaginacion - 1, totalPaginas);

                int cantidadAExcluir = (paginaNormalizada - 1) * registrosPorPaginaNormalizados;

                List<ProductModel> paginaDeProductos = productosFiltrados
                    .Skip(cantidadAExcluir)
                    .Take(registrosPorPaginaNormalizados)
                    .ToList();

                // Datos para la vista
                ViewBag.PaginaActual = paginaNormalizada;
                ViewBag.CantidadRegistrosPorPagina = registrosPorPaginaNormalizados;
                ViewBag.CantidadTotalPaginas = totalPaginas;
                ViewBag.PageWindowStart = inicioVentana;
                ViewBag.PageWindowEnd = finVentana;
                ViewBag.HasPrevPage = paginaNormalizada > 1;
                ViewBag.HasNextPage = paginaNormalizada < totalPaginas;

                return View(paginaDeProductos);
            }
            catch
            {
                // Falla controlada y mensaje amigable
                ModelState.AddModelError(string.Empty, "Ocurrió un error al cargar el catálogo.");
                ViewBag.PaginaActual = 1;
                ViewBag.CantidadRegistrosPorPagina = 5;
                ViewBag.TextoBusqueda = string.Empty;
                ViewBag.CantidadTotalPaginas = 1;
                ViewBag.PageWindowStart = 1;
                ViewBag.PageWindowEnd = 1;
                ViewBag.HasPrevPage = false;
                ViewBag.HasNextPage = false;
                return View(Enumerable.Empty<ProductModel>());
            }
        }

        // ===== Sanitización / bloqueo simple del filtro público =====
        private static (string textoSanitizado, string? mensajeBloqueoSeguridad) SanearTextoBusqueda(string textoBusquedaOriginal)
        {
            string textoRecortado = (textoBusquedaOriginal ?? string.Empty).Trim();

            // Limitar longitud para evitar payloads gigantes en un formulario público
            if (textoRecortado.Length > LongitudMaximaBusqueda)
            {
                textoRecortado = textoRecortado.Substring(0, LongitudMaximaBusqueda);
            }

            string textoEnMinusculas = textoRecortado.ToLowerInvariant();

            // Firmas “clásicas” de SQLi que NO tienen sentido en un buscador público
            string[] patronesSospechosos =
            {
                "--",
                "/*",
                "*/",
                ";",
                "xp_",
                "exec ",
                "execute ",
                "drop ",
                "alter ",
                "create ",
                "insert ",
                "update ",
                "delete ",
                "truncate ",
                "union ",
                "select "
            };

            foreach (string patron in patronesSospechosos)
            {
                if (textoEnMinusculas.Contains(patron))
                {
                    string mensajeBloqueo = $"Tu búsqueda contiene el patrón no permitido “{patron.Trim()}”. Se ha bloqueado por seguridad.";
                    return (textoRecortado, mensajeBloqueo);
                }
            }

            return (textoRecortado, null);
        }

        // ===== Utilitarios de normalización y relevancia =====
        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

            string textoDescompuesto = texto.Normalize(NormalizationForm.FormD);
            var acumulador = new StringBuilder(textoDescompuesto.Length);

            foreach (char caracter in textoDescompuesto)
            {
                var categoria = CharUnicodeInfo.GetUnicodeCategory(caracter);
                if (categoria != UnicodeCategory.NonSpacingMark)
                    acumulador.Append(caracter);
            }

            return acumulador.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private static (int empieza, int indice, int diferenciaLongitud) CalcularRelevancia(string baseNormalizada, string terminoNormalizado)
        {
            int empieza = baseNormalizada.StartsWith(terminoNormalizado, StringComparison.Ordinal) ? 0 : 1;
            int indice = baseNormalizada.IndexOf(terminoNormalizado, StringComparison.Ordinal);
            if (indice < 0) indice = int.MaxValue;
            int diferenciaLongitud = Math.Abs(baseNormalizada.Length - terminoNormalizado.Length);
            return (empieza, indice, diferenciaLongitud);
        }
    }
}
