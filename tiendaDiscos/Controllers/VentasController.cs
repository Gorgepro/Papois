using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using tiendaDiscos.Models;

namespace tiendaDiscos.Controllers
{
    public class VentasController : Controller
    {
        private readonly string conexion;

        public VentasController(IConfiguration configuration)
        {
            conexion = configuration.GetConnectionString("ConexionBD")
                ?? throw new InvalidOperationException("Falta la conexión ConexionBD.");
        }

        public IActionResult Index()
        {
            var modelo = new VentasViewModel();
            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();

            using (var comando = new MySqlCommand(@"
                SELECT Id_Producto,
                       CONCAT(Nombre, ' - $', Precio, ' (existencia: ', Existencia, ')') AS Nombre
                FROM Productos
                WHERE Existencia > 0 AND Activo = 1
                ORDER BY Nombre", conexionBD))
            using (var lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    modelo.Productos.Add(new OpcionViewModel
                    {
                        Id = lector.GetInt32("Id_Producto"),
                        Nombre = lector.GetString("Nombre")
                    });
                }
            }

            using (var comando = new MySqlCommand(@"
                SELECT v.Id_Venta, v.Fecha, v.Total_Precio,
                       COALESCE(GROUP_CONCAT(
                           CONCAT(p.Nombre, ' (', dv.Cantidad, ')')
                           ORDER BY p.Nombre SEPARATOR ', '
                       ), 'Sin detalle') AS Productos,
                       COALESCE(SUM(dv.Cantidad), 0) AS Cantidad
                FROM Ventas v
                LEFT JOIN Detalle_Venta dv ON dv.Id_Venta = v.Id_Venta
                LEFT JOIN Productos p ON p.Id_Producto = dv.Id_Producto
                GROUP BY v.Id_Venta, v.Fecha, v.Total_Precio
                ORDER BY v.Fecha DESC, v.Id_Venta DESC
                LIMIT 50", conexionBD))
            using (var lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    modelo.Ventas.Add(new VentaResumenViewModel
                    {
                        Id = lector.GetInt32("Id_Venta"),
                        Fecha = lector.GetDateTime("Fecha"),
                        Total = lector.GetDecimal("Total_Precio"),
                        Productos = lector.GetString("Productos"),
                        Cantidad = Convert.ToInt32(lector["Cantidad"])
                    });
                }
            }

            return View(modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(int idProducto, int cantidad)
        {
            if (idProducto <= 0 || cantidad <= 0)
            {
                TempData["Error"] = "Selecciona un producto e indica una cantidad mayor que cero.";
                return RedirectToAction(nameof(Index));
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            using var transaccion = conexionBD.BeginTransaction();

            decimal precio;
            int existencia;
            using (var comando = new MySqlCommand(
                "SELECT Precio, Existencia FROM Productos WHERE Id_Producto = @id AND Activo = 1 FOR UPDATE",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@id", idProducto);
                using var lector = comando.ExecuteReader();
                if (!lector.Read())
                {
                    transaccion.Rollback();
                    TempData["Error"] = "El producto seleccionado ya no existe.";
                    return RedirectToAction(nameof(Index));
                }
                precio = lector.GetDecimal("Precio");
                existencia = lector.GetInt32("Existencia");
            }

            if (existencia < cantidad)
            {
                transaccion.Rollback();
                TempData["Error"] = $"Existencia insuficiente. Disponible: {existencia}.";
                return RedirectToAction(nameof(Index));
            }
            if (precio > 99999999.99m / cantidad)
            {
                transaccion.Rollback();
                TempData["Error"] = "El total de la venta supera el máximo permitido por la base de datos.";
                return RedirectToAction(nameof(Index));
            }

            var subtotal = precio * cantidad;
            using (var comando = new MySqlCommand(
                "INSERT INTO Ventas (Total_Precio) VALUES (@total)",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@total", subtotal);
                comando.ExecuteNonQuery();
            }

            long idVenta;
            using (var comando = new MySqlCommand("SELECT LAST_INSERT_ID()", conexionBD, transaccion))
            {
                idVenta = Convert.ToInt64(comando.ExecuteScalar());
            }

            using (var comando = new MySqlCommand(@"
                INSERT INTO Detalle_Venta
                    (Id_Venta, Id_Producto, Cantidad, Precio_Unitario, Subtotal)
                VALUES
                    (@venta, @producto, @cantidad, @precio, @subtotal)",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@venta", idVenta);
                comando.Parameters.AddWithValue("@producto", idProducto);
                comando.Parameters.AddWithValue("@cantidad", cantidad);
                comando.Parameters.AddWithValue("@precio", precio);
                comando.Parameters.AddWithValue("@subtotal", subtotal);
                comando.ExecuteNonQuery();
            }

            using (var comando = new MySqlCommand(
                "UPDATE Productos SET Existencia = Existencia - @cantidad WHERE Id_Producto = @id",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@cantidad", cantidad);
                comando.Parameters.AddWithValue("@id", idProducto);
                comando.ExecuteNonQuery();
            }

            transaccion.Commit();
            TempData["Mensaje"] = $"Venta #{idVenta} registrada correctamente.";
            return RedirectToAction(nameof(Index));
        }
    }
}
