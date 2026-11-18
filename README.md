# Blockchain.CSharp

Librería .NET para registrar datos en una **cadena de bloques sólo de agregado**: cada bloque guarda el hash del anterior, de modo que cualquier alteración, borrado o reordenamiento de un registro se puede detectar.

Sirve para dar **integridad verificable** (evidencia de alteración) a cualquier sistema que lleve un historial: movimientos de trazabilidad, auditorías, logs, transacciones, etc.

> **Qué no hace:** no replica datos ni mejora la disponibilidad del sistema, y no es una red distribuida con consenso. Detecta alteraciones; no las impide. Ver [Seguridad: anclaje externo](#seguridad-anclaje-externo).

## Requisitos

- .NET 8 o .NET 10 (la librería compila para ambos; se recomienda .NET 10, ya que .NET 8 está fuera de soporte desde noviembre de 2026).

## Estructura

| Proyecto | Descripción |
|---|---|
| `Blockchain.CSharp` | La librería. |
| `Blockchain.CSharp.ConsoleApp` | Demo: crea una cadena en archivo, la reabre, la altera y muestra la detección. |
| `Blockchain.CSharp.Tests` | Pruebas xUnit (funcionamiento, alteraciones, persistencia y concurrencia). |

```powershell
dotnet build Blockchain.CSharp.sln
dotnet test  Blockchain.CSharp.sln
dotnet run --project Blockchain.CSharp.ConsoleApp
```

## Uso rápido

```csharp
using System.Text;
using Blockchain.CSharp;

var chain = new BlockChain();                       // en memoria, crea el bloque génesis
chain.Add(Encoding.UTF8.GetBytes("Despacho lote 123"));
chain.Add(Encoding.UTF8.GetBytes("Recepción lote 123"));

var resultado = chain.Validate();
Console.WriteLine(resultado);                       // "Válida (3 bloques)"
```

Si la cadena fue alterada, `Validate()` informa el primer bloque inválido y el motivo:

```text
Inválida en bloque #3: El contenido del bloque no coincide con su hash.
```

### Persistencia en archivo

```csharp
using var store = new FileBlockStore(@"C:\datos\cadena.jsonl");
var chain = new BlockChain(store: store);          // si el archivo tiene bloques, los carga
chain.Add(datos);
```

`FileBlockStore` guarda un bloque por línea (JSON Lines), fuerza la escritura a disco en cada bloque, se recupera si la última línea quedó cortada por una caída y toma el archivo en exclusiva (un solo proceso escritor).

## Opciones

```csharp
var opciones = new BlockChainOptions
{
    HashAlgorithm  = BlockHashAlgorithm.Sha256, // Sha256 (por defecto), Sha384, Sha512
    DifficultyBits = 0,                         // prueba de trabajo: bits en cero al inicio del hash
    GenesisData    = [],                        // datos del bloque génesis
    TimeProvider   = TimeProvider.System,       // reloj (inyectable en pruebas)
};
var chain = new BlockChain(opciones, store);
```

- **El algoritmo y la dificultad no se pueden cambiar en una cadena existente:** hay que abrirla siempre con las mismas opciones con las que se creó.
- **Prueba de trabajo:** en un sistema con una sola autoridad no agrega seguridad, sólo encarece la escritura. Se deja en 0 salvo que haya un motivo concreto. Cada bit duplica el costo; el máximo soportado es 32. El minado se puede cancelar con un `CancellationToken` en `Add`.

## Seguridad: anclaje externo

Quien tenga acceso de escritura al almacenamiento puede alterar un bloque **y recalcular todos los siguientes**; la cadena resultante es internamente válida. Para detectarlo, guardá periódicamente el hash del último bloque **fuera del sistema** (otro servidor, un log firmado, un correo, un servicio de sellado de tiempo) y validá contra él:

```csharp
// Al anclar
var hashAnclado  = chain.HeadHash.ToArray();
var indiceAnclado = chain.Last.Index;
// ...guardar ambos fuera del sistema...

// Al auditar
var resultado = chain.Validate(hashAnclado, indiceAnclado);
// Detecta reescritura completa y truncamiento de la cadena.
```

## Almacenamiento propio

Para guardar la cadena en una base de datos u otro medio, implementá `IBlockStore`:

```csharp
public interface IBlockStore
{
    long Count { get; }
    Block? Last { get; }
    Block Get(long index);
    IEnumerable<Block> ReadAll();
    void Append(Block block);
}
```

Contrato de `Append`: debe rechazar con `BlockStoreConcurrencyException` un bloque cuyo `Index` no sea exactamente `Count`. En una base de datos alcanza con una **clave única sobre el índice** (traduciendo la violación de clave a esa excepción). Así, si varios procesos escriben a la vez, la cadena nunca se bifurca: `BlockChain` reintenta con el nuevo último bloque.

Para rehidratar bloques leídos del almacenamiento se usa el constructor público:

```csharp
new Block(index, timestamp, data, prevHash, nonce, hash);
```

No hace falta confiar en lo leído: `Validate()` verifica todo.

## Qué se valida

`Validate()` recorre la cadena completa y comprueba, para cada bloque:

1. Que los índices sean consecutivos desde 0 (detecta borrados y reordenamientos).
2. Que el génesis no tenga hash previo y que cada bloque apunte al hash del anterior.
3. Que las fechas no retrocedan.
4. Que el hash recalculado coincida con el guardado (detecta cambios en datos, fecha o nonce).
5. Que el hash tenga el largo del algoritmo configurado y cumpla la dificultad.

## Formato del hash

El hash se calcula sobre una serialización binaria canónica (little-endian), independiente de la cultura, el huso horario y la plataforma:

```text
versión(1) | índice(8) | ticks UTC(8) | nonce(8) | largo hashPrevio(4) | hashPrevio | largo datos(4) | datos
```

Los campos de largo evitan ambigüedades entre el hash previo y los datos. Este formato es parte del contrato: cambiarlo invalida las cadenas existentes.

## Concurrencia

- `BlockChain` y los almacenamientos incluidos son seguros para usar desde varios hilos.
- `FileBlockStore` admite un único proceso escritor por archivo.
- Para varios procesos o réplicas, usá un almacenamiento propio que cumpla el contrato de `Append` (ver arriba).
