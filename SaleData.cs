using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using MyBox;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

namespace DiscountsAndSales
{
    internal class SaleData
    {
        private List<Sale> itemsOnSale;
        private List<int> debugItems;

        private Plugin _plugin;

        private const int MAX_ITEMS_ON_SALE = 8;
        private static SaleData inst = null;
        

        public SaleData()
        {
            itemsOnSale = new List<Sale>();
            debugItems = new List<int>();

        }

        public void SetPlugin(Plugin plugin)
        {
            this._plugin = plugin;
        }

        public static SaleData Instance
        {
            get
            {
                if (inst == null)
                    inst = new SaleData();

                return inst;
            }
        }

        public void LoadData(SaveManager saveManager)
        {
            _plugin.LogInfo("Loading sale data");
            ES3Settings es3_settings = ES3Settings.defaultSettings;

            string filePath = es3_settings.FullPath;
            _plugin.LogInfo("Settings file path " + filePath);
            FileInfo saveFileInfo = new FileInfo(filePath);

            if (!Directory.Exists(saveFileInfo.Directory.FullName + @"\DiscountsAndSales"))
                Directory.CreateDirectory(saveFileInfo.Directory.FullName + @"\DiscountsAndSales");
            if (!File.Exists(saveFileInfo.Directory.FullName + @"\DiscountsAndSales\sale_data.json"))
            {
                //File.Create(saveFileInfo.Directory.FullName + @"\DiscountsAndSales\sale_data.json").Close();
                SaleDataObject input = new SaleDataObject();
                input.saleList = new List<Sale>();
                input.debugProducts = new List<int>();
                string output = JsonConvert.SerializeObject(input);
                using (StreamWriter sw = new StreamWriter(saveFileInfo.Directory.FullName + @"\DiscountsAndSales\sale_data.json"))
                {
                    sw.Write(output);
                }
            }

            SaleDataObject saleDataObject = JsonConvert.DeserializeObject<SaleDataObject>(File.ReadAllText(saveFileInfo.Directory.FullName + @"\DiscountsAndSales\sale_data.json"));
            itemsOnSale = saleDataObject.saleList;
            debugItems = saleDataObject.debugProducts;

            if (itemsOnSale == null) 
            {
                _plugin.LogError("Item sale list is null, erasing sale data and falling back.");
                SaleDataObject cleanInput = new SaleDataObject();
                cleanInput.saleList = new List<Sale>();
                cleanInput.debugProducts = new List<int>();
                string output = JsonConvert.SerializeObject(cleanInput);
                using (StreamWriter sw = new StreamWriter(saveFileInfo.Directory.FullName + @"\DiscountsAndSales\sale_data.json"))
                {
                    sw.Write(output);
                }
            }

        }

        public void SaveData(SaveManager saveManager)
        {
            _plugin.LogInfo("Saving sale data");
            ES3Settings es3_settings = ES3Settings.defaultSettings;
            
            string filePath = es3_settings.FullPath;
            _plugin.LogInfo("Settings file path " + filePath);
            FileInfo saveFileInfo = new FileInfo(filePath);

            if (!Directory.Exists(saveFileInfo.Directory.FullName + @"\DiscountsAndSales"))
                Directory.CreateDirectory(saveFileInfo.Directory.FullName + @"\DiscountsAndSales");
            if (!File.Exists(saveFileInfo.Directory.FullName + @"\DiscountsAndSales\sale_data.json"))
                File.Create(saveFileInfo.Directory.FullName + @"\DiscountsAndSales\sale_data.json").Close();

            SaleDataObject saleDataObject = new SaleDataObject();
            saleDataObject.saleList = itemsOnSale;
            saleDataObject.debugProducts = debugItems;
            string output = JsonConvert.SerializeObject(saleDataObject);
            using (StreamWriter sw = new StreamWriter(saveFileInfo.Directory.FullName + @"\DiscountsAndSales\sale_data.json"))
            {
                sw.Write(output);
            }
        }

        public List<Sale> GetItemsOnSale()
        {
            return itemsOnSale;
        }

        public List<int> GetDebugItems()
        {
            return debugItems;
        }

        public bool CanItemBePlacedOnSale(int productID)
        {
            ProductSO productSO = Singleton<IDManager>.Instance.ProductSO(productID);
            PriceManager priceManager = Singleton<PriceManager>.Instance;
            float currentCost = priceManager.CurrentCost(productID);
            float marketPrice = (float)Math.Round((double)(currentCost + currentCost * productSO.OptimumProfitRate / 100f), 2);
            float newPrice = CrossoverClass.Round(marketPrice * _plugin.GetFixedDiscountMultiplier(), 1);

            return (newPrice - currentCost) >= _plugin.GetProfitLimit(); // If new price makes at least $1 profit per item
        }

        public bool IsMaxProductsOnSale()
        {
            return itemsOnSale.Count >= MAX_ITEMS_ON_SALE;
        }

        public void DoTamperChecks()
        {
            int count = 0;
            List<Sale> itemsToRemove = new List<Sale>();
            itemsOnSale.ForEach(sale =>
            {
                // Tamper Checks
                if (count >= MAX_ITEMS_ON_SALE)
                {
                    _plugin.LogError("Sale data has been tampered with. There is a MAXIMUM of " + MAX_ITEMS_ON_SALE + ", skipping product ID: " + sale);
                    itemsToRemove.Add(sale);
                }

                if (!CanItemBePlacedOnSale(sale.productID))
                {
                    _plugin.LogError("Sale data has been tampered with. Products are in sale data that cannot be discounted, skipping product ID: " + sale);
                    itemsToRemove.Add(sale);
                }

                count++;
            });

            itemsToRemove.ForEach(sale =>
            {
                itemsOnSale.Remove(sale);
            });
        }
    }
}
