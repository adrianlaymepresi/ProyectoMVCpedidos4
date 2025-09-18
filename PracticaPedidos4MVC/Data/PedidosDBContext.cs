using Microsoft.EntityFrameworkCore;
using PracticaPedidos4MVC.Models;
using System;

namespace PracticaPedidos4MVC.Data
{
    public class PedidosDBContext: DbContext
    {
        public PedidosDBContext(DbContextOptions<PedidosDBContext> options) : base(options) { }

        public DbSet<UserModel> Users { get; set; }
        public DbSet<ProductModel> Products { get; set; }
        public DbSet<OrderModel> Orders { get; set; }
        public DbSet<OrderItemModel> OrderItems { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<OrderModel>()
                .HasOne(o => o.Cliente)
                .WithMany()
                .HasForeignKey(o => o.IdCliente)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OrderItemModel>()
                .HasOne(oi => oi.Producto)
                .WithMany()
                .HasForeignKey(oi => oi.IdProducto)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OrderItemModel>()
                .HasOne(oi => oi.Pedido)
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.IdPedido)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductModel>()
                .Property(p => p.Precio)
                .HasColumnType("decimal(8,2)");

            modelBuilder.Entity<OrderItemModel>()
                .Property(oi => oi.Subtotal)
                .HasColumnType("decimal(9,2)");

            modelBuilder.Entity<OrderModel>()
                .Property(o => o.Total)
                .HasColumnType("decimal(9,2)");

        }
    }
}
