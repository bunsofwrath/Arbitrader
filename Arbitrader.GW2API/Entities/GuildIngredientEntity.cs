﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Arbitrader.GW2API.Results;

namespace Arbitrader.GW2API.Entities
{
    [Table("GuildIngredients")]
    public class GuildIngredientEntity : Entity
    {
        public int UpgradeID { get; set; }
        public int? Count { get; set; }

        public static implicit operator GuildIngredientEntity(GuildIngredientResult result)
        {
            return (GuildIngredientEntity)result.ToEntity();
        }
    }
}