using System.ComponentModel.DataAnnotations;

namespace PracticaPedidos4MVC.Models
{
    public class OrderModel
    {
        [Display(Name = "Identificador Pedido")]
        public int Id { get; set; }

        [Display(Name = "Cliente")]
        public int IdCliente { get; set; }

        [Display(Name = "Fecha del Pedido")]
        public DateTime Fecha { get; set; }

        [Display(Name = "Estado del Pedido")]
        public string Estado { get; set; }

        [Display(Name = "Total del Pedido")]
        public decimal Total { get; set; }
    }
}
