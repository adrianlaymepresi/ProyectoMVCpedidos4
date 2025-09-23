using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class OrderItemModel
    {
        [Display(Name = "Identificador Ítem de Pedido")]
        public int Id { get; set; }

        [Display(Name = "Pedido")]
        [Required, Range(1, int.MaxValue)]
        // Debe referenciar a un pedido válido (FK)
        public int IdPedido { get; set; }

        [Display(Name = "Producto")]
        [Required, Range(1, int.MaxValue)]
        // Debe referenciar a un producto válido (FK)
        public int IdProducto { get; set; }

        [Display(Name = "Cantidad")]
        [Required, Range(1, 100000)]
        // Al menos 1 unidad
        public int Cantidad { get; set; }

        [Display(Name = "Subtotal")]
        [ValidateNever]
        [Range(0.01, 9_999_999.99)]
        // Precio * Cantidad; permite centavos
        public decimal Subtotal { get; set; }

        public OrderModel? Pedido { get; set; }
        public ProductModel? Producto { get; set; }
    }
}
