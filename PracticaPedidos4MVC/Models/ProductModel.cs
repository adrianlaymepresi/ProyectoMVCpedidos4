using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class ProductModel
    {
        [Display(Name = "Identificador Producto")]
        public int Id { get; set; }

        [Display(Name = "Nombre del Producto")]
        [Required, StringLength(120, MinimumLength = 4)]
        public string Nombre { get; set; }

        [Display(Name = "Descripción del Producto")]
        [StringLength(1000, MinimumLength = 4)]
        public string Descripcion { get; set; }

        [Display(Name = "Categoría")]
        [StringLength(60, MinimumLength = 3)]
        // Opcional; si se escribe, al menos 3
        public string? Categoria { get; set; }

        [Display(Name = "Precio del Producto")]
        [Required, Range(0.01, 999999.99)]
        public decimal Precio { get; set; }

        [Display(Name = "Stock del Producto")]
        [Required, Range(0, 100000)]
        public int Stock { get; set; }
    }
}
