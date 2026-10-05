using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RRDA.Core.Validator
{    
    /// <summary>
    /// Rappresenta una cella identificata da coordinate dirette (non da DefinedName).
    /// </summary>
    public class CellMapping
    {
        /// <summary>
        /// Coordinate della cella in formato Excel (es: "C3", "foglio!$B$3").
        /// </summary>
        public string Coordinate { get; set; } = string.Empty;

        /// <summary>
        /// Alias da usare come chiave nell'indice dei DefinedNames virtuale.
        /// Funge da nome simbolico per il sistema di importazione.
        /// </summary>
        public string DefinedNameAlias { get; set; } = string.Empty;
    }
}
