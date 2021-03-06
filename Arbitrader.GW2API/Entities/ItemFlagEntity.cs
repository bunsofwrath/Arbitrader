﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arbitrader.GW2API.Results;

namespace Arbitrader.GW2API.Entities
{
    /// <summary>
    /// A row of data in the ItemFlags table. Associated with the result type <see cref="ItemFlagResult"/>.
    /// </summary>
    [Table("ItemFlags")]
    public class ItemFlagEntity : Entity
    {
        /// <summary>
        /// Gets or sets the name of the item flag.
        /// </summary>
        public string Name { get; set; }
    }
}
