using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class ProductModel
    {
        [Display(Name = "Identificador Producto")]
        public int Id { get; set; }

        [Display(Name = "Nombre del Producto")]
        public string Nombre { get; set; }

        [Display(Name = "Descripción del Producto")]
        public string Descripcion { get; set; }

        [Display(Name = "Precio del Producto")]
        public decimal Precio { get; set; }

        [Display(Name = "Stock del Producto")]
        public int Stock { get; set; }
    }
}
