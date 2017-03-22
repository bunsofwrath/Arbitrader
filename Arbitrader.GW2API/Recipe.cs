//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated from a template.
//
//     Manual changes to this file may cause unexpected behavior in your application.
//     Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Arbitrader.GW2API
{
    using System;
    using System.Collections.Generic;
    
    public partial class Recipe
    {
        public int pk { get; set; }
        public int id { get; set; }
        public string type { get; set; }
        public int outputItemPK { get; set; }
        public Nullable<int> outputItemCount { get; set; }
        public int recipeDisciplinePK { get; set; }
        public Nullable<int> minRating { get; set; }
        public int ingredientsPK { get; set; }
        public System.DateTime loadDate { get; set; }
    
        public virtual Ingredient Ingredient { get; set; }
        public virtual Item Item { get; set; }
        public virtual RecipeDiscipline RecipeDiscipline { get; set; }
    }
}
