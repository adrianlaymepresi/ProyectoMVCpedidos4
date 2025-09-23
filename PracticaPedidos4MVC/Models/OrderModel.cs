using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class OrderModel
    {
        [Display(Name = "Identificador Pedido")]
        public int Id { get; set; }

        [Display(Name = "Cliente")]
        [Required, Range(1, int.MaxValue)]
        // Debe referenciar a un usuario válido (FK)
        public int IdCliente { get; set; }

        [Display(Name = "Fecha del Pedido")]
        [Required]
        // Fecha obligatoria del pedido
        public DateTime Fecha { get; set; }

        [Display(Name = "Estado del Pedido")]
        [ValidateNever]
        [StringLength(20, MinimumLength = 7)]
        // Valores esperados: Pendiente, Procesado, Enviado, Entregado
        public string Estado { get; set; }

        [Display(Name = "Total del Pedido")]
        [ValidateNever]
        [Range(0.00, 9_999_999.99)]
        // Suma de subtotales; permite 0.00 si aún no se cargan ítems
        public decimal Total { get; set; }

        public UserModel? Cliente { get; set; }
        public ICollection<OrderItemModel>? Items { get; set; }
    }
}
