using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class OrderItemModel
    {
        [Display(Name = "Identificador Ítem de Pedido")]
        public int Id { get; set; }

        [Display(Name = "Producto")]
        public int IdProducto { get; set; }

        [Display(Name = "Cantidad")]
        public int Cantidad { get; set; }

        [Display(Name = "Subtotal")]
        public decimal Subtotal { get; set; }

        public ProductModel? Producto { get; set; }
    }
}
