namespace tiendaDiscos.Models
{
    public class OpcionViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
    }

    public class ProductoInventarioViewModel
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Categoria { get; set; } = "";
        public string Artista { get; set; } = "";
        public decimal Precio { get; set; }
        public int Existencia { get; set; }
        public int StockMinimo { get; set; }
    }

    public class ProveedorInventarioViewModel
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public string Telefono { get; set; } = "";
        public string Correo { get; set; } = "";
        public string Direccion { get; set; } = "";
    }

    public class InventarioViewModel
    {
        public List<ProductoInventarioViewModel> Productos { get; set; } = new();
        public List<OpcionViewModel> Categorias { get; set; } = new();
        public string Buscar { get; set; } = "";
        public int? CategoriaSeleccionada { get; set; }
        public int TotalProductos { get; set; }
        public int TotalUnidades { get; set; }
        public int StockBajo { get; set; }
    }

    public class ProductoFormViewModel
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public int IdCategoria { get; set; }
        public string? Artista { get; set; }
        public decimal Precio { get; set; }
        public int Existencia { get; set; }
        public int StockMinimo { get; set; } = 5;
        public List<OpcionViewModel> Categorias { get; set; } = new();
    }

    public class VentaResumenViewModel
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Productos { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Total { get; set; }
    }

    public class VentasViewModel
    {
        public List<OpcionViewModel> Productos { get; set; } = new();
        public List<VentaResumenViewModel> Ventas { get; set; } = new();
    }

    public class EntradaResumenViewModel
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Proveedor { get; set; } = "";
        public string Productos { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Total { get; set; }
        public string Observaciones { get; set; } = "";
    }

    public class PedidosViewModel
    {
        public List<OpcionViewModel> Proveedores { get; set; } = new();
        public List<ProveedorInventarioViewModel> ProveedoresDetalle { get; set; } = new();
        public List<OpcionViewModel> Productos { get; set; } = new();
        public List<EntradaResumenViewModel> Entradas { get; set; } = new();
    }
}
