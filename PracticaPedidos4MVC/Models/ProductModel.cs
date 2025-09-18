using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class ProductModel
    {
        [Display(Name = "Identificador Producto")]
        public int Id { get; set; }

        [Display(Name = "Nombre del Producto")]
        [Required, StringLength(120, MinimumLength = 4)]
        // Ej.: "Olla" como caso mínimo lógico
        public string Nombre { get; set; }

        [Display(Name = "Descripción del Producto")]
        [StringLength(1000, MinimumLength = 4)]
        // Opcional; texto corto y claro, sin obligar
        public string Descripcion { get; set; }

        [Display(Name = "Precio del Producto")]
        [Required, Range(0.01, 999999.99)]
        // Desde 0.01 Bs hasta 999 999.99 Bs
        public decimal Precio { get; set; }

        [Display(Name = "Stock del Producto")]
        [Required, Range(0, 100000)]
        // Permite 0 cuando no hay unidades disponibles
        public int Stock { get; set; }
    }
}
