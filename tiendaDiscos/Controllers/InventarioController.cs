using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using tiendaDiscos.Models;

namespace tiendaDiscos.Controllers
{
    public class InventarioController : Controller
    {
        string conexion;

        public InventarioController(IConfiguration configuration)
        {
            conexion = configuration.GetConnectionString("ConexionBD")
                ?? throw new InvalidOperationException("Falta la conexión ConexionBD.");
        }

        public IActionResult Index(string? buscar = "", int? categoria = null)
        {
            var modelo = new InventarioViewModel
            {
                Buscar = buscar?.Trim() ?? "",
                CategoriaSeleccionada = categoria
            };

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();

            const string consultaProductos = @"
                SELECT p.Id_Producto, p.Codigo, p.Nombre, c.Nombre_Categoria,
                       p.Artista, p.Precio, p.Existencia, p.Stock_Minimo
                FROM Productos p
                INNER JOIN Categorias c ON c.Id_Categoria = p.Id_Categoria
                WHERE p.Activo = 1
                  AND (p.Nombre LIKE CONCAT('%', @buscar, '%')
                       OR p.Codigo LIKE CONCAT('%', @buscar, '%')
                       OR COALESCE(p.Artista, '') LIKE CONCAT('%', @buscar, '%'))
                  AND (@categoria = 0 OR p.Id_Categoria = @categoria)
                ORDER BY p.Nombre";

            using (var comando = new MySqlCommand(consultaProductos, conexionBD))
            {
                comando.Parameters.AddWithValue("@buscar", modelo.Buscar);
                comando.Parameters.AddWithValue("@categoria", categoria ?? 0);

                using var lector = comando.ExecuteReader();
                while (lector.Read())
                {
                    var producto = new ProductoInventarioViewModel
                    {
                        Id = lector.GetInt32("Id_Producto"),
                        Codigo = lector.GetString("Codigo"),
                        Nombre = lector.GetString("Nombre"),
                        Categoria = lector.GetString("Nombre_Categoria"),
                        Artista = lector.IsDBNull(4) ? "" : lector.GetString("Artista"),
                        Precio = lector.GetDecimal("Precio"),
                        Existencia = lector.GetInt32("Existencia"),
                        StockMinimo = lector.GetInt32("Stock_Minimo")
                    };
                    modelo.Productos.Add(producto);
                    modelo.TotalUnidades += producto.Existencia;
                    if (producto.Existencia <= producto.StockMinimo)
                    {
                        modelo.StockBajo++;
                    }
                }
            }
            modelo.TotalProductos = modelo.Productos.Count;

            using (var comando = new MySqlCommand(
                "SELECT Id_Categoria, Nombre_Categoria FROM Categorias ORDER BY Nombre_Categoria",
                conexionBD))
            using (var lector = comando.ExecuteReader())
            {
                while (lector.Read())
                {
                    modelo.Categorias.Add(new OpcionViewModel
                    {
                        Id = lector.GetInt32("Id_Categoria"),
                        Nombre = lector.GetString("Nombre_Categoria")
                    });
                }
            }

            return View(modelo);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(CargarFormularioProducto());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(ProductoFormViewModel modelo)
        {
            NormalizarProducto(modelo);
            if (!ValidarProducto(modelo))
            {
                modelo.Categorias = CargarCategorias();
                return View(modelo);
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            using (var comandoCategoria = new MySqlCommand(
                "SELECT COUNT(*) FROM Categorias WHERE Id_Categoria = @id",
                conexionBD))
            {
                comandoCategoria.Parameters.AddWithValue("@id", modelo.IdCategoria);
                if (Convert.ToInt32(comandoCategoria.ExecuteScalar()) == 0)
                {
                    ModelState.AddModelError(nameof(modelo.IdCategoria), "La categoría seleccionada ya no existe.");
                    modelo.Categorias = CargarCategorias();
                    return View(modelo);
                }
            }
            const string consulta = @"
                INSERT INTO Productos
                    (Codigo, Nombre, Id_Categoria, Artista, Precio, Existencia, Stock_Minimo)
                VALUES
                    (@codigo, @nombre, @categoria, @artista, @precio, @existencia, @stockMinimo)";

            try
            {
                using var comando = new MySqlCommand(consulta, conexionBD);
                comando.Parameters.AddWithValue("@codigo", modelo.Codigo);
                comando.Parameters.AddWithValue("@nombre", modelo.Nombre);
                comando.Parameters.AddWithValue("@categoria", modelo.IdCategoria);
                comando.Parameters.AddWithValue("@artista", string.IsNullOrWhiteSpace(modelo.Artista)
                    ? DBNull.Value
                    : modelo.Artista);
                comando.Parameters.AddWithValue("@precio", modelo.Precio);
                comando.Parameters.AddWithValue("@existencia", modelo.Existencia);
                comando.Parameters.AddWithValue("@stockMinimo", modelo.StockMinimo);
                comando.ExecuteNonQuery();
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                ModelState.AddModelError(nameof(modelo.Codigo), "Ya existe un producto con ese código.");
                modelo.Categorias = CargarCategorias();
                return View(modelo);
            }

            TempData["Mensaje"] = "Producto agregado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            using var comando = new MySqlCommand(@"
                SELECT Id_Producto, Codigo, Nombre, Id_Categoria, Artista,
                       Precio, Existencia, Stock_Minimo
                FROM Productos
                WHERE Id_Producto = @id AND Activo = 1", conexionBD);
            comando.Parameters.AddWithValue("@id", id);
            ProductoFormViewModel modelo;
            using (var lector = comando.ExecuteReader())
            {
                if (!lector.Read())
                {
                    return NotFound();
                }

                modelo = new ProductoFormViewModel
                {
                    Id = lector.GetInt32("Id_Producto"),
                    Codigo = lector.GetString("Codigo"),
                    Nombre = lector.GetString("Nombre"),
                    IdCategoria = lector.GetInt32("Id_Categoria"),
                    Artista = lector.IsDBNull(4) ? "" : lector.GetString("Artista"),
                    Precio = lector.GetDecimal("Precio"),
                    Existencia = lector.GetInt32("Existencia"),
                    StockMinimo = lector.GetInt32("Stock_Minimo")
                };
            }

            modelo.Categorias = CargarCategorias();
            return View("Create", modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, ProductoFormViewModel modelo)
        {
            if (id <= 0 || id != modelo.Id)
            {
                return BadRequest();
            }

            NormalizarProducto(modelo);
            if (!ValidarProducto(modelo))
            {
                modelo.Categorias = CargarCategorias();
                return View("Create", modelo);
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            using (var comandoCategoria = new MySqlCommand(
                "SELECT COUNT(*) FROM Categorias WHERE Id_Categoria = @id",
                conexionBD))
            {
                comandoCategoria.Parameters.AddWithValue("@id", modelo.IdCategoria);
                if (Convert.ToInt32(comandoCategoria.ExecuteScalar()) == 0)
                {
                    ModelState.AddModelError(nameof(modelo.IdCategoria), "La categoría seleccionada ya no existe.");
                    modelo.Categorias = CargarCategorias();
                    return View("Create", modelo);
                }
            }

            const string consulta = @"
                UPDATE Productos
                SET Codigo = @codigo, Nombre = @nombre, Id_Categoria = @categoria,
                    Artista = @artista, Precio = @precio, Existencia = @existencia,
                    Stock_Minimo = @stockMinimo
            WHERE Id_Producto = @id AND Activo = 1";
            try
            {
                using var comando = new MySqlCommand(consulta, conexionBD);
                comando.Parameters.AddWithValue("@codigo", modelo.Codigo);
                comando.Parameters.AddWithValue("@nombre", modelo.Nombre);
                comando.Parameters.AddWithValue("@categoria", modelo.IdCategoria);
                comando.Parameters.AddWithValue("@artista", string.IsNullOrWhiteSpace(modelo.Artista)
                    ? DBNull.Value
                    : modelo.Artista);
                comando.Parameters.AddWithValue("@precio", modelo.Precio);
                comando.Parameters.AddWithValue("@existencia", modelo.Existencia);
                comando.Parameters.AddWithValue("@stockMinimo", modelo.StockMinimo);
                comando.Parameters.AddWithValue("@id", id);
                if (comando.ExecuteNonQuery() == 0)
                {
                    TempData["Error"] = "El producto ya no existe.";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                ModelState.AddModelError(nameof(modelo.Codigo), "Ya existe un producto con ese código.");
                modelo.Categorias = CargarCategorias();
                return View("Create", modelo);
            }

            TempData["Mensaje"] = "Producto actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
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
                    "DELETE FROM Productos WHERE Id_Producto = @id AND Activo = 1",
                    conexionBD);
                comando.Parameters.AddWithValue("@id", id);
                if (comando.ExecuteNonQuery() == 0)
                {
                    TempData["Error"] = "El producto no existe o ya no está disponible.";
                }
                else
                {
                    TempData["Mensaje"] = "Producto eliminado correctamente.";
                }
            }
            catch (MySqlException ex) when (ex.Number == 1451)
            {
                using var comando = new MySqlCommand(
                    "UPDATE Productos SET Activo = 0 WHERE Id_Producto = @id AND Activo = 1",
                    conexionBD);
                comando.Parameters.AddWithValue("@id", id);
                if (comando.ExecuteNonQuery() == 0)
                {
                    TempData["Error"] = "El producto no existe o ya no está disponible.";
                }
                else
                {
                    TempData["Mensaje"] = "El producto tiene registros relacionados; se ocultó del inventario y se conservó su historial.";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddCategory(string nombre)
        {
            nombre = nombre?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 50)
            {
                TempData["Error"] = "Escribe un nombre de categoría de máximo 50 caracteres.";
                return RedirectToAction(nameof(Index));
            }

            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            const string consulta = @"
                INSERT INTO Categorias (Nombre_Categoria)
                SELECT @nombre
                WHERE NOT EXISTS (
                    SELECT 1 FROM Categorias WHERE Nombre_Categoria = @nombre
                )";
            using var comando = new MySqlCommand(consulta, conexionBD);
            comando.Parameters.AddWithValue("@nombre", nombre);
            if (comando.ExecuteNonQuery() == 0)
            {
                TempData["Error"] = "Esa categoría ya existe.";
            }
            else
            {
                TempData["Mensaje"] = "Categoría agregada correctamente.";
            }

            return RedirectToAction(nameof(Index));
        }

        private ProductoFormViewModel CargarFormularioProducto()
        {
            return new ProductoFormViewModel { Categorias = CargarCategorias() };
        }

        private static void NormalizarProducto(ProductoFormViewModel modelo)
        {
            modelo.Codigo = modelo.Codigo?.Trim() ?? "";
            modelo.Nombre = modelo.Nombre?.Trim() ?? "";
            modelo.Artista = modelo.Artista?.Trim();
        }

        private bool ValidarProducto(ProductoFormViewModel modelo)
        {
            if (string.IsNullOrWhiteSpace(modelo.Codigo))
            {
                ModelState.AddModelError(nameof(modelo.Codigo), "El código es obligatorio.");
            }
            if (string.IsNullOrWhiteSpace(modelo.Nombre))
            {
                ModelState.AddModelError(nameof(modelo.Nombre), "El nombre es obligatorio.");
            }
            if (modelo.Codigo.Length > 30 || modelo.Nombre.Length > 100)
            {
                ModelState.AddModelError("", "El código admite hasta 30 caracteres y el nombre hasta 100.");
            }
            if (modelo.Artista?.Length > 100)
            {
                ModelState.AddModelError(nameof(modelo.Artista), "El artista admite hasta 100 caracteres.");
            }
            if (modelo.IdCategoria <= 0)
            {
                ModelState.AddModelError(nameof(modelo.IdCategoria), "Selecciona una categoría.");
            }
            if (modelo.Precio < 0 || modelo.Precio > 99999999.99m
                || modelo.Precio != decimal.Round(modelo.Precio, 2)
                || modelo.Existencia < 0 || modelo.StockMinimo < 0)
            {
                ModelState.AddModelError("", "El precio debe tener hasta dos decimales y estar entre 0 y 99,999,999.99; las existencias no pueden ser negativas.");
            }

            return ModelState.IsValid;
        }

        private List<OpcionViewModel> CargarCategorias()
        {
            var categorias = new List<OpcionViewModel>();
            using var conexionBD = new MySqlConnection(conexion);
            conexionBD.Open();
            using var comando = new MySqlCommand(
                "SELECT Id_Categoria, Nombre_Categoria FROM Categorias ORDER BY Nombre_Categoria",
                conexionBD);
            using var lector = comando.ExecuteReader();
            while (lector.Read())
            {
                categorias.Add(new OpcionViewModel
                {
                    Id = lector.GetInt32("Id_Categoria"),
                    Nombre = lector.GetString("Nombre_Categoria")
                });
            }
            return categorias;
        }
    }
}
