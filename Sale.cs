using System;
using System.Collections.Generic;
using System.Text;

namespace DiscountsAndSales
{
    public class Sale
    {
        public int productID;
        public float previousPrice = 0f;
        public int percentageDiscount = 0;

        public Sale()
        {

        }

        public Sale(int productID, float previousPrice, int percentageDiscount)
        {
            this.productID = productID;
            this.previousPrice = previousPrice;
            this.percentageDiscount = percentageDiscount;
        }   
    }
}
