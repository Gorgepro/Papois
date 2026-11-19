using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using tiendaDiscos.Models;

namespace tiendaDiscos.Controllers
{
    public class PedidosController : Controller
    {
        private readonly string conexion;

        public PedidosController(IConfiguration configuration)
        {
            conexion = configuration.GetConnectionString("ConexionBD")
                ?? throw new InvalidOperationException("Falta la conexión ConexionBD.");
        }

        public IActionResult Index()
        {
            var modelo = new PedidosViewModel();
            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();

            using (var comando = new MySqlCommand(
                @"SELECT Id_Proveedor, Nombre_Proveedor, Telefono, Correo, Direccion
                  FROM Proveedores
                  WHERE Activo = 1
                  ORDER BY Nombre_Proveedor",
                conexionBD))
            using (var lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    var id = lector.GetInt32("Id_Proveedor");
                    var nombre = lector.GetString("Nombre_Proveedor");
                    modelo.Proveedores.Add(new OpcionViewModel
                    {
                        Id = id,
                        Nombre = nombre
                    });
                    modelo.ProveedoresDetalle.Add(new ProveedorInventarioViewModel
                    {
                        Id = id,
                        Nombre = nombre,
                        Telefono = lector.IsDBNull(2) ? "" : lector.GetString("Telefono"),
                        Correo = lector.IsDBNull(3) ? "" : lector.GetString("Correo"),
                        Direccion = lector.IsDBNull(4) ? "" : lector.GetString("Direccion")
                    });
                }
            }

            using (var comando = new MySqlCommand(
                "SELECT Id_Producto, Nombre FROM Productos WHERE Activo = 1 ORDER BY Nombre",
                conexionBD))
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
                SELECT e.Id_Entrada, e.Fecha_Entrada, e.Total_Compra,
                       e.Observaciones, pr.Nombre_Proveedor,
                       COALESCE(GROUP_CONCAT(
                           CONCAT(p.Nombre, ' (', de.Cantidad_Recibida, ')')
                           ORDER BY p.Nombre SEPARATOR ', '
                       ), 'Sin detalle') AS Productos,
                       COALESCE(SUM(de.Cantidad_Recibida), 0) AS Cantidad
                FROM Entradas e
                INNER JOIN Proveedores pr ON pr.Id_Proveedor = e.Id_Proveedor
                LEFT JOIN Detalle_Entrada de ON de.Id_Entrada = e.Id_Entrada
                LEFT JOIN Productos p ON p.Id_Producto = de.Id_Producto
                GROUP BY e.Id_Entrada, e.Fecha_Entrada, e.Total_Compra,
                         e.Observaciones, pr.Nombre_Proveedor
                ORDER BY e.Fecha_Entrada DESC, e.Id_Entrada DESC
                LIMIT 50", conexionBD))
            using (var lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    modelo.Entradas.Add(new EntradaResumenViewModel
                    {
                        Id = lector.GetInt32("Id_Entrada"),
                        Fecha = lector.GetDateTime("Fecha_Entrada"),
                        Total = lector.GetDecimal("Total_Compra"),
                        Proveedor = lector.GetString("Nombre_Proveedor"),
                        Productos = lector.GetString("Productos"),
                        Cantidad = Convert.ToInt32(lector["Cantidad"]),
                        Observaciones = lector.IsDBNull(3) ? "" : lector.GetString("Observaciones")
                    });
                }
            }

            return View(modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(int idProveedor, int idProducto, int cantidad, decimal precioCompra, string? observaciones)
        {
            observaciones = observaciones?.Trim();
            if (idProveedor <= 0 || idProducto <= 0 || cantidad <= 0
                || precioCompra < 0 || precioCompra > 99999999.99m
                || precioCompra != decimal.Round(precioCompra, 2))
            {
                TempData["Error"] = "Completa proveedor, producto y cantidad; el precio debe estar entre 0 y 99,999,999.99 con máximo dos decimales.";
                return RedirectToAction(nameof(Index));
            }
            if (observaciones?.Length > 250)
            {
                TempData["Error"] = "Las observaciones no pueden superar 250 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            using var transaccion = conexionBD.BeginTransaction();

            using (var comando = new MySqlCommand(
                "SELECT Id_Producto FROM Productos WHERE Id_Producto = @id AND Activo = 1 FOR UPDATE",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@id", idProducto);
                if (comando.ExecuteScalar() is null)
                {
                    transaccion.Rollback();
                    TempData["Error"] = "El producto seleccionado ya no existe.";
                    return RedirectToAction(nameof(Index));
                }
            }

            using (var comando = new MySqlCommand(
                "SELECT COUNT(*) FROM Proveedores WHERE Id_Proveedor = @id AND Activo = 1",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@id", idProveedor);
                if (Convert.ToInt32(comando.ExecuteScalar()) == 0)
                {
                    transaccion.Rollback();
                    TempData["Error"] = "El proveedor seleccionado ya no existe.";
                    return RedirectToAction(nameof(Index));
                }
            }

            if (precioCompra > 99999999.99m / cantidad)
            {
                transaccion.Rollback();
                TempData["Error"] = "El total de la entrada supera el máximo permitido por la base de datos.";
                return RedirectToAction(nameof(Index));
            }

            var total = precioCompra * cantidad;
            using (var comando = new MySqlCommand(@"
                INSERT INTO Entradas (Id_Proveedor, Total_Compra, Observaciones)
                VALUES (@proveedor, @total, @observaciones)",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@proveedor", idProveedor);
                comando.Parameters.AddWithValue("@total", total);
                comando.Parameters.AddWithValue("@observaciones", string.IsNullOrWhiteSpace(observaciones)
                    ? DBNull.Value
                    : observaciones);
                comando.ExecuteNonQuery();
            }

            long idEntrada;
            using (var comando = new MySqlCommand("SELECT LAST_INSERT_ID()", conexionBD, transaccion))
            {
                idEntrada = Convert.ToInt64(comando.ExecuteScalar());
            }

            using (var comando = new MySqlCommand(@"
                INSERT INTO Detalle_Entrada
                    (Id_Entrada, Id_Producto, Cantidad_Recibida, Precio_Compra)
                VALUES
                    (@entrada, @producto, @cantidad, @precio)",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@entrada", idEntrada);
                comando.Parameters.AddWithValue("@producto", idProducto);
                comando.Parameters.AddWithValue("@cantidad", cantidad);
                comando.Parameters.AddWithValue("@precio", precioCompra);
                comando.ExecuteNonQuery();
            }

            using (var comando = new MySqlCommand(
                "UPDATE Productos SET Existencia = Existencia + @cantidad WHERE Id_Producto = @id",
                conexionBD,
                transaccion))
            {
                comando.Parameters.AddWithValue("@cantidad", cantidad);
                comando.Parameters.AddWithValue("@id", idProducto);
                comando.ExecuteNonQuery();
            }

            transaccion.Commit();
            TempData["Mensaje"] = $"Entrada #{idEntrada} registrada correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateSupplier(string nombre, string? telefono, string? correo, string? direccion)
        {
            if (!ValidarProveedor(ref nombre, ref telefono, ref correo, ref direccion))
            {
                return RedirectToAction(nameof(Index));
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            using var comando = new MySqlCommand(@"
                INSERT INTO Proveedores (Nombre_Proveedor, Telefono, Correo, Direccion)
                VALUES (@nombre, @telefono, @correo, @direccion)", conexionBD);
            comando.Parameters.AddWithValue("@nombre", nombre);
            comando.Parameters.AddWithValue("@telefono", string.IsNullOrWhiteSpace(telefono) ? DBNull.Value : telefono);
            comando.Parameters.AddWithValue("@correo", string.IsNullOrWhiteSpace(correo) ? DBNull.Value : correo);
            comando.Parameters.AddWithValue("@direccion", string.IsNullOrWhiteSpace(direccion) ? DBNull.Value : direccion);
            comando.ExecuteNonQuery();

            TempData["Mensaje"] = "Proveedor agregado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateSupplier(int id, string nombre, string? telefono, string? correo, string? direccion)
        {
            if (id <= 0)
            {
                return BadRequest();
            }
            if (!ValidarProveedor(ref nombre, ref telefono, ref correo, ref direccion))
            {
                return RedirectToAction(nameof(Index));
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            using (var existe = new MySqlCommand(
                "SELECT COUNT(*) FROM Proveedores WHERE Id_Proveedor = @id AND Activo = 1",
                conexionBD))
            {
                existe.Parameters.AddWithValue("@id", id);
                if (Convert.ToInt32(existe.ExecuteScalar()) == 0)
                {
                    TempData["Error"] = "El proveedor ya no existe.";
                    return RedirectToAction(nameof(Index));
                }
            }

            using (var comando = new MySqlCommand(@"
                UPDATE Proveedores
                SET Nombre_Proveedor = @nombre, Telefono = @telefono,
                    Correo = @correo, Direccion = @direccion
                WHERE Id_Proveedor = @id AND Activo = 1", conexionBD))
            {
                comando.Parameters.AddWithValue("@nombre", nombre);
                comando.Parameters.AddWithValue("@telefono", string.IsNullOrWhiteSpace(telefono) ? DBNull.Value : telefono);
                comando.Parameters.AddWithValue("@correo", string.IsNullOrWhiteSpace(correo) ? DBNull.Value : correo);
                comando.Parameters.AddWithValue("@direccion", string.IsNullOrWhiteSpace(direccion) ? DBNull.Value : direccion);
                comando.Parameters.AddWithValue("@id", id);
                comando.ExecuteNonQuery();
            }

            TempData["Mensaje"] = "Proveedor actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteSupplier(int id)
        {
            if (id <= 0)
            {
                return BadRequest();
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            try
            {
                using var comando = new MySqlCommand(
                    "DELETE FROM Proveedores WHERE Id_Proveedor = @id AND Activo = 1",
                    conexionBD);
                comando.Parameters.AddWithValue("@id", id);
                if (comando.ExecuteNonQuery() == 0)
                {
                    TempData["Error"] = "El proveedor no existe o ya no está disponible.";
                }
                else
                {
                    TempData["Mensaje"] = "Proveedor eliminado correctamente.";
                }
            }
            catch (MySqlException ex) when (ex.Number == 1451)
            {
                using var comando = new MySqlCommand(
                    "UPDATE Proveedores SET Activo = 0 WHERE Id_Proveedor = @id AND Activo = 1",
                    conexionBD);
                comando.Parameters.AddWithValue("@id", id);
                if (comando.ExecuteNonQuery() == 0)
                {
                    TempData["Error"] = "El proveedor no existe o ya no está disponible.";
                }
                else
                {
                    TempData["Mensaje"] = "El proveedor tiene entradas relacionadas; se ocultó de las listas y se conservó su historial.";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ValidarProveedor(ref string nombre, ref string? telefono, ref string? correo, ref string? direccion)
        {
            nombre = nombre?.Trim() ?? "";
            telefono = telefono?.Trim();
            correo = correo?.Trim();
            direccion = direccion?.Trim();
            if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            {
                TempData["Error"] = "El nombre del proveedor es obligatorio y no debe superar 100 caracteres.";
                return false;
            }
            if (telefono?.Length > 20 || correo?.Length > 100 || direccion?.Length > 200)
            {
                TempData["Error"] = "Revisa la longitud del teléfono, correo o dirección.";
                return false;
            }

            return true;
        }
    }
}
