using BepInEx;
using UnityEngine;
using MyBox;
using BepInEx.Configuration;
using System.IO;
using HarmonyLib;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using TMPro;
using System.Collections;
using UnityEngine.UIElements;
using System.Linq;
using System.Runtime.CompilerServices;
using System;
using UnityEngine.UI;
using BepInEx.Logging;

namespace DiscountsAndSales
{

    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private SaleData saleData;

        private float _fixedDiscountMultiplier = 0.8f;
        private int _fixedDiscount = 20;
        private float _profitLimit = 1f;
        private KeyCode saleKeyBind = KeyCode.T;

        private Texture2D _carboardTexture;
        private GameObject _windowSaleSign;

        private ConfigEntry<int> configPercentageDiscount;
        

        private void Awake()
        {
            configPercentageDiscount = Config.Bind("Discounts And Sales", "PercentageDiscount", 20, "The percentage discount for items on sale. Must be a whole number value, greater than 0 and is recommended not to be above 30% (for now)");
            ConfigEntry<string> configKeybind;  configKeybind = Config.Bind("Discounts And Sales", "SaleKeybind", "T", "The keybind to set an item on sale. Case sensitive and single letters must be capital. For a list of key codes, see https://docs.unity3d.com/ScriptReference/KeyCode.html");
            ConfigEntry<float> configProfitLimit = Config.Bind("Discounts And Sales", "ProfitLimit", 1f, "The minimum profit a discounted item must have to be allowed to discount, default is $1.00");
            _profitLimit = configProfitLimit.Value;

            if (configPercentageDiscount.Value == 0 || configPercentageDiscount.Value > 100)
            {
                Logger.LogError($"There was a problem gathering config information. The percentage discount value: {configPercentageDiscount.Value} is invalid.");
                // Use default values
            } else
            {
                _fixedDiscount = configPercentageDiscount.Value;
                _fixedDiscountMultiplier = 1 - ((float)_fixedDiscount / 100);
            }

            Logger.LogInfo($"Config Info: Percentage Discount = {_fixedDiscount}");
            Logger.LogInfo($"Set new fixed discount multiplier: {_fixedDiscountMultiplier}");

            if (!KeyCode.TryParse(configKeybind.Value.ToString(), out saleKeyBind))
            {
                Logger.LogError("Unable to decipher config keybind value, falling back to T");
            }

            this.saleData = SaleData.Instance;
            saleData.SetPlugin(this);

            var harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll(typeof(Plugin));
            harmony.GetPatchedMethods().ForEach(m =>
            {
                Logger.LogInfo("Patched Method " + m.Name);
            });

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Load carboard tex

            byte[] array = DiscountsAndSales.Embedded.Resources.Embedded.texCardboard;
            Texture2D texture2D = new Texture2D(2, 2);
            ImageConversion.LoadImage(texture2D, array);
            _carboardTexture = texture2D;

        }


       
        private void Update()
        {
            // Set values
            
            CrossoverClass.ProductsOnSale = saleData.GetItemsOnSale();
            CrossoverClass.DebugProducts = saleData.GetDebugItems();

            if (SceneManager.GetActiveScene().name == "Main Scene")
            {
                UpdateUIPatch();

                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    Logger.LogInfo("---- ITEM DEBUG STATS ----");
                    int[] data = new int[500];
                    foreach (int product in CrossoverClass.debugStats)
                    {
                        data[product] += 1;
                    }

                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i] != 0 && i != 0)
                        {
                            string productName = Singleton<IDManager>.Instance.ProductSO(i).name;
                            Logger.LogInfo("Stats for " + productName + " : " + data[i]);
                        }
                    }
                }

                if (Input.GetKeyDown(saleKeyBind))
                {
                    GameObject settingPriceCanvas = GameObject.Find("Setting Price Canvas");
                    GameObject menu = settingPriceCanvas.transform.Find("Menu").gameObject;
                    if (menu.activeSelf)
                    {
                        if (saleData.GetItemsOnSale().Contains(CrossoverClass.CurrentProductID))
                            TakeOffSale(CrossoverClass.CurrentProductID);
                        else
                            PlaceOnSale(CrossoverClass.CurrentProductID);
                    }
                }

                if (Input.GetKeyDown(KeyCode.LeftAlt))
                {
                    Logger.LogInfo("CURRENT PRODUCT ID FOR " + Singleton<IDManager>.Instance.ProductSO(CrossoverClass.CurrentProductID).name + " IS " + CrossoverClass.CurrentProductID.ToString());
                }

                // AutoPriceUpdater hard-coded keybind
                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.R))
                {
                    StartCoroutine(WaitUntilAPUHasUpdatedPrices());
                }

                DisplayManager displayManager = Singleton<DisplayManager>.Instance;

                saleData.GetItemsOnSale().ForEach(item =>
                {
                    displayManager.GetLabeledEmptyDisplaySlots(item).ForEach(displaySlot =>
                    {
                        GameObject saleTag = displaySlot.gameObject.transform.Find("Sale Tag").gameObject;
                        saleTag.SetActive(false);
                    });
                });
            }

        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode lsm)
        {
            if (scene.name == "Main Scene")
            {
                SaleData.Instance.LoadData(Singleton<SaveManager>.Instance);
                List<int> itemsOnSale = saleData.GetItemsOnSale();

                itemsOnSale.ForEach(item =>
                {
                    Logger.LogInfo(item.ToString() + " is on sale");
                });

                StartCoroutine(WaitUntilLoaded());
            }
        }

        IEnumerator WaitUntilLoaded()
        {
            yield return new WaitUntil(() => { return Singleton<RackManager>.Instance != null; });
            saleData.DoTamperChecks();
            AddSaleTags();
            DoVisualPricesFix();
            CreateUIPatch();


            // Change price of on-sale products
            PriceManager priceManager = Singleton<PriceManager>.Instance;
            
            saleData.GetItemsOnSale().ForEach(item =>
            {
                ProductSO productSO = Singleton<IDManager>.Instance.ProductSO(item);
                float currentCost = priceManager.CurrentCost(item);
                float marketPrice = (float)Math.Round((double)(currentCost + currentCost * productSO.OptimumProfitRate / 100f), 2);
                float newPrice = CrossoverClass.Round(marketPrice * _fixedDiscountMultiplier, 1);

                priceManager.PriceSet(new Pricing(item, newPrice));
            });

            DayCycleManager dayCycleManager = Singleton<DayCycleManager>.Instance;
            dayCycleManager.OnStartedNewDay += OnStartedNewDay;



        }

        private void AddSaleTags()
        {
            DisplayManager displayManager = Singleton<DisplayManager>.Instance;

            GameObject windowSaleTag = null;

            displayManager.m_Displays.ForEach(display =>
            {
                display.m_DisplaySlots.ForEach(displaySlot =>
                {
                    GameObject priceTagObject = displaySlot.m_PriceTag.gameObject;
                    GameObject saleTag = UnityEngine.Object.Instantiate(priceTagObject);

                    /*if (displaySlot.gameObject.transform.parent.transform.parent.gameObject.name.Contains("Pallet")) // Ensures compatibility with Dydy's pallet mod
                    {
                        Display display = displayManager.m_Displays.First(display => !display.gameObject.name.Contains("Pallet"));
                        saleTag = UnityEngine.Object.Instantiate(display.m_DisplaySlots.First().m_PriceTag.gameObject);
                    } else
                    {
                        saleTag = UnityEngine.Object.Instantiate(priceTagObject);
                    }*/

                    saleTag.transform.SetParent(displaySlot.gameObject.transform);
                    saleTag.transform.SetLocalPositionAndRotation(new Vector3(priceTagObject.transform.localPosition.x - 0.1f, priceTagObject.transform.localPosition.y, priceTagObject.transform.localPosition.z), Quaternion.identity);
                    saleTag.transform.rotation = priceTagObject.transform.rotation;
                    saleTag.name = "Sale Tag";

                    UnityEngine.GameObject.Destroy(saleTag.GetComponent<PriceTag>());

                    GameObject cube = saleTag.transform.Find("Visuals").transform.Find("Cube").gameObject;
                    cube.GetComponent<MeshRenderer>().material.color = Color.red;

                    GameObject saleCanvas = saleTag.transform.Find("Price Canvas").gameObject;
                    saleCanvas.name = "Sale Canvas";

                    bool isPallet = displaySlot.gameObject.transform.parent.transform.parent.gameObject.name.Contains("Pallet");
                    if (!isPallet) // Ensures compatibility with Dydy's pallet mod
                        UnityEngine.Object.Destroy(saleCanvas.transform.Find("BG").gameObject);

                    GameObject saleTextObject = saleCanvas.transform.Find("Text (TMP)").gameObject;
                    saleTextObject.name = "Sale Text";
                    TextMeshProUGUI saleTmPro = saleTextObject.GetComponent<TextMeshProUGUI>();
                    saleTmPro.text = "SALE!";
                    saleTmPro.color = Color.white;

                    if (!isPallet)
                    {
                        GameObject discountTextObject = UnityEngine.Object.Instantiate(saleTextObject);
                        discountTextObject.name = "Discount Text";
                        discountTextObject.transform.SetParent(saleCanvas.transform);
                        discountTextObject.transform.SetLocalPositionAndRotation(new Vector3(0, -0.01f, 0), Quaternion.identity);
                        discountTextObject.transform.rotation = saleTextObject.transform.rotation;
                        TextMeshProUGUI discountTmPro = discountTextObject.GetComponent<TextMeshProUGUI>();
                        discountTmPro.text = configPercentageDiscount.Value.ToString() + "% OFF";
                        discountTmPro.enableWordWrapping = false;
                    }

                    if (windowSaleTag == null && !isPallet)
                        windowSaleTag = UnityEngine.Object.Instantiate(saleTag);

                    saleTag.SetActive(false);
                });
            });

            GameObject gameHeader = GameObject.Find("---GAME---");
            GameObject tbeObject = gameHeader.transform.Find("Store").transform.Find("Store").transform.Find("Sections").transform.Find("Section 2").transform.GetChild(1).gameObject;
            windowSaleTag.transform.SetParent(tbeObject.transform);

            if (Singleton<SaveManager>.Instance.Progression.StoreUpgradeLevel > 0)
                windowSaleTag.transform.SetLocalPositionAndRotation(new Vector3(0.201f, 1.285f, -6.12f), Quaternion.identity);
            else
                windowSaleTag.transform.SetLocalPositionAndRotation(new Vector3(0.1003f, 1.3f, -3.48f), Quaternion.identity);

            windowSaleTag.transform.localScale = new Vector3(1f, 7f, 7f);

            windowSaleTag.transform.Find("Sale Canvas").transform.Find("Discount Text").gameObject.transform.SetLocalPositionAndRotation(new Vector3(0f, -0.01f, 0f), Quaternion.identity);

            GameObject cube = windowSaleTag.transform.Find("Visuals").transform.Find("Cube").gameObject;         
            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            renderer.material.mainTexture = _carboardTexture;
            windowSaleTag.SetActive(false);
            _windowSaleSign = windowSaleTag;
        }

        private void PlaceOnSale(int productID)
        {
            if (saleData.IsMaxProductsOnSale())
            {
                CrossoverClass.CustomWarning("Max products on sale");
                return;
            }

            if (!saleData.CanItemBePlacedOnSale(productID))
            {
                CrossoverClass.CustomWarning("A discount will make no profit on this item.");
                return;
            }

            saleData.GetItemsOnSale().Add(CrossoverClass.CurrentProductID);

            // Visual price tag fix
            DisplayManager displayManager = Singleton<DisplayManager>.Instance;
            ProductSO productSO = Singleton<IDManager>.Instance.ProductSO(productID);
            displayManager.GetDisplaySlots(productID, true).ForEach(displaySlot =>
            {
                PriceTag priceTag = displaySlot.m_PriceTag;
                GameObject priceTagObject = priceTag.gameObject;
                TMP_Text priceTmPro = priceTag.m_PriceText;
                priceTmPro.color = Color.red;

                displaySlot.gameObject.transform.Find("Sale Tag").gameObject.SetActive(true);
            });

            // Change price
            PriceManager priceManager = Singleton<PriceManager>.Instance;
            float currentCost = priceManager.CurrentCost(productID);
            float marketPrice = (float)Math.Round((double)(currentCost + currentCost * productSO.OptimumProfitRate / 100f), 2);
            float newPrice = CrossoverClass.Round(marketPrice * _fixedDiscountMultiplier, 1);

            priceManager.PriceSet(new Pricing(productID, newPrice));
            GameObject settingPriceCanvas = GameObject.Find("Setting Price Canvas");
            GameObject menu = settingPriceCanvas.transform.Find("Menu").gameObject;
            GameObject window = menu.transform.Find("Window").gameObject;
            GameObject priceInput = window.transform.Find("Price Panel").transform.Find("Price Info").transform.Find("Price Input").gameObject;
            Logger.LogInfo("Price change: " + newPrice.ToString());
            priceInput.GetComponent<MoneyInputRestrictor>().OnEndEdit(newPrice.ToString());

            if (!_windowSaleSign.activeSelf)
                _windowSaleSign.SetActive(true);
        }

        private void TakeOffSale(int productID)
        {
            saleData.GetItemsOnSale().Remove(CrossoverClass.CurrentProductID);
            DisplayManager displayManager = Singleton<DisplayManager>.Instance;
            displayManager.GetDisplaySlots(productID, true).ForEach(displaySlot =>
            {
                PriceTag priceTag = displaySlot.m_PriceTag;
                GameObject priceTagObject = priceTag.gameObject;
                TMP_Text priceTmPro = priceTag.m_PriceText;
                priceTmPro.color = Color.black;

                displaySlot.gameObject.transform.Find("Sale Tag").gameObject.SetActive(false);
            });

            // Change price
            ProductSO productSO = Singleton<IDManager>.Instance.ProductSO(productID);
            PriceManager priceManager = Singleton<PriceManager>.Instance;
            float currentCost = priceManager.CurrentCost(productID);
            float marketPrice = (float)Math.Round((double)(currentCost + currentCost * productSO.OptimumProfitRate / 100f), 2);

            priceManager.PriceSet(new Pricing(productID, marketPrice));
            GameObject settingPriceCanvas = GameObject.Find("Setting Price Canvas");
            GameObject menu = settingPriceCanvas.transform.Find("Menu").gameObject;
            GameObject window = menu.transform.Find("Window").gameObject;
            GameObject priceInput = window.transform.Find("Price Panel").transform.Find("Price Info").transform.Find("Price Input").gameObject;
            Logger.LogInfo("Price change: " + marketPrice.ToString());
            priceInput.GetComponent<MoneyInputRestrictor>().OnEndEdit(marketPrice.ToString());

            if (saleData.GetItemsOnSale().Count <= 0)
            {
                if (_windowSaleSign.activeSelf)
                    _windowSaleSign.SetActive(false);
            }
        }

        private void DoVisualPricesFix()
        {
            List<int> itemsOnSale = saleData.GetItemsOnSale();

            DisplayManager displayManager = Singleton<DisplayManager>.Instance;
            foreach (Display display in displayManager.m_Displays)
            {
                foreach (DisplaySlot displaySlot in display.m_DisplaySlots)
                {

                    if (!itemsOnSale.Contains(displaySlot.ProductID))
                        continue;
                    PriceTag priceTag = displaySlot.m_PriceTag;
                    GameObject priceTagObject = priceTag.gameObject;
                    TMP_Text priceTmPro = priceTag.m_PriceText;
                    priceTmPro.color = Color.red;

                    displaySlot.gameObject.transform.Find("Sale Tag").gameObject.SetActive(true);
                }
            }

            if (saleData.GetItemsOnSale().Count <= 0)
            {
                _windowSaleSign.SetActive(false);
            } else
            {
                _windowSaleSign.SetActive(true);
            }
        }

        private void CreateUIPatch()
        {
            GameObject settingPriceCanvas = GameObject.Find("Setting Price Canvas");
            GameObject menu = settingPriceCanvas.transform.Find("Menu").gameObject;
            GameObject window = menu.transform.Find("Window").gameObject;
            GameObject pricePanel = window.transform.Find("Price Panel").gameObject;
            GameObject productInfo = pricePanel.transform.Find("Product Info").gameObject;

            Transform productIconT = productInfo.transform.Find("Product Icon").transform;
            Transform productNameT = productInfo.transform.Find("Product Name").transform;

            productIconT.SetLocalPositionAndRotation(new Vector3(0, 40, 0), Quaternion.identity);
            productNameT.SetLocalPositionAndRotation(new Vector3(0, -20, 0), Quaternion.identity);

            GameObject saleText = UnityEngine.Object.Instantiate(productNameT.gameObject);
            saleText.transform.SetParent(productInfo.transform);
            saleText.name = "Sale Text";
            TextMeshProUGUI saleTextTmPro = saleText.GetComponent<TextMeshProUGUI>();
            saleTextTmPro.text = "On Sale?";
            saleTextTmPro.fontSize = 18;
            saleTextTmPro.fontSizeMax = 18;
            saleText.transform.SetLocalPositionAndRotation(new Vector3(0, -50, 0), Quaternion.identity);
            CrossoverClass.SaleText = saleText;

            GameObject saleInstruction = UnityEngine.Object.Instantiate(saleText);
            saleInstruction.transform.SetParent(productInfo.transform);
            
            saleInstruction.AddComponent<UIScaleAnimation>();
            UIScaleAnimation animation = saleInstruction.GetComponent<UIScaleAnimation>();
            animation.m_AnimatedObject = (RectTransform)saleInstruction.transform;
            animation.m_AnimationScale = 1.1f;
            animation.m_AnimationSpeed = 0.5f;

            saleInstruction.name = "Sale Instruction";
            TextMeshProUGUI saleInstructionTmPro = saleInstruction.GetComponent<TextMeshProUGUI>();
            saleInstructionTmPro.text = "TOGGLE SALE: " + saleKeyBind.ToString().ToUpper();
            saleInstructionTmPro.fontSize = 12;
            saleInstructionTmPro.fontSizeMin = 12;
            saleInstructionTmPro.fontSizeMax = 12;
            saleInstructionTmPro.color = Color.green;
            saleInstruction.transform.SetLocalPositionAndRotation(new Vector3(0, -75, 0), Quaternion.identity);
        }

        private void UpdateUIPatch()
        {
            GameObject settingPriceCanvas = GameObject.Find("Setting Price Canvas");
            GameObject menu = settingPriceCanvas.transform.Find("Menu").gameObject;
            if (menu.activeSelf)
            {
                GameObject saleText = CrossoverClass.SaleText;
                TextMeshProUGUI saleTextTmPro = saleText.GetComponent<TextMeshProUGUI>();
                GameObject window = menu.transform.Find("Window").gameObject;
                GameObject priceInput = window.transform.Find("Price Panel").transform.Find("Price Info").transform.Find("Price Input").gameObject;
                TMP_InputField inputField = priceInput.GetComponent<TMP_InputField>();

                GameObject saleInstruction = window.transform.Find("Price Panel").transform.Find("Product Info").transform.Find("Sale Instruction").gameObject;
                TextMeshProUGUI saleInstructionTmPro = saleInstruction.GetComponent<TextMeshProUGUI>();
                
                if (saleData.CanItemBePlacedOnSale(CrossoverClass.CurrentProductID))
                {
                    saleInstructionTmPro.text = "TOGGLE SALE: " + saleKeyBind.ToString().ToUpper();
                    saleInstructionTmPro.color = Color.green;
                } else
                {
                    saleInstructionTmPro.text = "CANNOT DISCOUNT";
                    saleInstructionTmPro.color = Color.red;
                }

                if (saleData.GetItemsOnSale().Contains(CrossoverClass.CurrentProductID))
                {
                    saleTextTmPro.text = "On Sale? Yes";
                    inputField.readOnly = true;
                }
                else
                {
                    saleTextTmPro.text = "On Sale? No";
                    inputField.readOnly = false;
                }


            }
        }

        public void LogError(string msg)
        {
            Logger.LogError(msg);
        }

        public void LogInfo(string msg)
        {
            Logger.LogInfo(msg);
        }

        [HarmonyPatch(typeof(PriceTag), nameof(PriceTag.InstantInteract))]
        [HarmonyPrefix]
        static void PTInstantInteractPrefix(PriceTag __instance)
        {
            CrossoverClass.CurrentProductID = __instance.m_DisplaySlot.ProductID;
            
        }

        // Shopping List Patch

        [HarmonyPatch(typeof(Customer), nameof(Customer.StartShopping))]
        [HarmonyPostfix]
        static void CustomerStartShoppingPostfix(Customer __instance)
        {
            Debug.Log("Harmony Patch: Customer Started Shopping");
            List<int> productsOnSale = CrossoverClass.ProductsOnSale;
            ItemQuantity shoppingList = __instance.ShoppingList;

            DisplayManager displayManager = Singleton<DisplayManager>.Instance;

            Dictionary<int, int> newShoppingList = CrossoverClass.DuplicateDoubleIntDictionary(shoppingList.Products); // to avoid enumeration errors

            shoppingList.Products.Keys.ForEach(key =>
            {
                ProductSO productSO = Singleton<IDManager>.Instance.ProductSO(key);
                int randomCeiling = productSO.GridLayoutInStorage.productCount >= 6 ? 4 : 3;
                int productRandomCeiling = 0;

                if (productSO.GridLayoutInStorage.productCount <= 6)
                {
                    productRandomCeiling = 2;
                }
                else if (productSO.GridLayoutInStorage.productCount <= 16)
                {
                    productRandomCeiling = (productSO.GridLayoutInStorage.productCount / 2) - 3;

                    if (productRandomCeiling < 0)
                        productRandomCeiling = 2;
                } else
                {
                    productRandomCeiling = 6;
                }

                // Flip an either 1 in 4 chance or 1 in 3 chance of them actually picking up the product
                if (UnityEngine.Random.RandomRangeInt(0, randomCeiling) == 1)
                {
                    int quantity = shoppingList.Products[key];
                    int newQuantity = 0;
                    newQuantity += quantity;
                    if (productsOnSale.Contains(key))
                    {
                        if (quantity < 3)
                        {
                            int quantToAdd = UnityEngine.Random.RandomRangeInt(1, productRandomCeiling);
                            newQuantity += quantToAdd;
                            newShoppingList[key] = quantity;
                            Debug.Log("Patched Customer for On-Sale Item >> Item: " + key + "  Quantity Added:" + quantToAdd);
                        }
                    }
                } else
                {
                    Debug.Log("Patched Customer for On-Sale Item >> Item: " + key + "  Failed flip, no product added.");
                }

                
            });

            productsOnSale.ForEach(productID =>
            {
                if (!shoppingList.Products.Keys.Contains(productID))
                {
                    ProductSO productSO = Singleton<IDManager>.Instance.ProductSO(productID);
                    int productRandomCeiling = 0;
                    if (productSO.GridLayoutInStorage.productCount <= 16)
                    {
                        productRandomCeiling = (productSO.GridLayoutInStorage.productCount / 2) - 4;

                        if (productRandomCeiling < 0)
                            productRandomCeiling = 2;
                    }
                    else
                    {
                        productRandomCeiling = 6;
                    }
                    int randomCeiling = productSO.GridLayoutInStorage.productCount >= 6 ? 4 : 3;

                    // Flip an either 1 in 3 chance or 1 in 2 chance of them actually picking up the product
                    if (UnityEngine.Random.RandomRangeInt(0, randomCeiling) == 1)
                    {
                        int quantToAdd = UnityEngine.Random.RandomRangeInt(1, productRandomCeiling + 2); // Add two on as they don't have any of the product beforehand
                        newShoppingList.Add(productID, quantToAdd);
                        Debug.Log("Patched Customer for On-Sale Item >> Item: " + productID + "  Blank Slate >> Quantity Added:" + quantToAdd);
                    }
                    else
                    {
                        Debug.Log("Patched Customer for On-Sale Item >> Item: " + productID + "  Failed flip, no product added.");
                    }
                }
            });

            shoppingList.Products = newShoppingList;

            shoppingList.Products.Keys.ForEach((key) =>
            {
                CrossoverClass.DebugProducts.ForEach(product =>
                {
                    if (product == key)
                    {
                        CrossoverClass.debugStats.Add(key);
                    }
                });

            });
        }

        // APU Fixes

        private void OnStartedNewDay() 
        {
            StartCoroutine(WaitUntilAPUHasUpdatedPrices());
        }

        IEnumerator WaitUntilAPUHasUpdatedPrices()
        {
            yield return new WaitForSeconds(2);

            // Update prices back to normal
            PriceManager priceManager = Singleton<PriceManager>.Instance;

            saleData.GetItemsOnSale().ForEach(item =>
            {
                Logger.LogInfo($"Conflict with AutoPriceUpdater, resetting on-sale price for Product ID: {item}");
                ProductSO productSO = Singleton<IDManager>.Instance.ProductSO(item);
                if (productSO != null)
                {
                    float currentCost = priceManager.CurrentCost(item);
                    float marketPrice = (float)Math.Round((double)(currentCost + currentCost * productSO.OptimumProfitRate / 100f), 2);
                    float newPrice = CrossoverClass.Round(marketPrice * _fixedDiscountMultiplier, 1);

                    priceManager.PriceSet(new Pricing(item, newPrice));
                }
            });
        }

        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
        [HarmonyPostfix]
        static void SaveManagerSavePostfix(SaveManager __instance)
        {
            SaleData.Instance.SaveData(__instance);
        }

        [HarmonyPatch(typeof(DisplaySlot), nameof(DisplaySlot.AddProduct))]
        [HarmonyPostfix]
        static void DisplaySlotAddProductPostfix(DisplaySlot __instance)
        {
            CrossoverClass.ProductsOnSale.ForEach(product =>
            {
                if (product == __instance.ProductID)
                {
                    GameObject saleTag = __instance.gameObject.transform.Find("Sale Tag").gameObject;
                    if (!saleTag.activeSelf)
                        saleTag.SetActive(true);
                }
            });
        }

        public float GetFixedDiscountMultiplier() { return _fixedDiscountMultiplier; }
        public float GetProfitLimit() { return _profitLimit; }
    }

    // Class to allow communication between harmony patches and private code
    public static class CrossoverClass
    {
        // Current product ID set from pricing UI
        public static int CurrentProductID = 0;

        // Sale data
        public static List<int> ProductsOnSale = new List<int>();
        public static List<int> DebugProducts = new List<int>();

        public static List<int> debugStats = new List<int>();

        // Changeable UI elements
        public static GameObject SaleText = null;

        public static float Round(float value, int digits)
        {
            float mult = Mathf.Pow(10.0f, (float)digits);
            return Mathf.Round(value * mult) / mult;
        }

        public static Dictionary<int, int> DuplicateDoubleIntDictionary(Dictionary<int, int> duplicates)
        {
            Dictionary<int, int> returnValue = new Dictionary<int, int>();
            foreach (int key in duplicates.Keys)
            {
                returnValue.Add(key, duplicates[key]);
            }
            return returnValue;
        }

        public static void CustomWarning(string text)
        {
            Singleton<WarningSystem>.Instance.RaiseInteractionWarning(InteractionWarningType.FULL_RACK, null);
            GameObject warningCanvas = GameObject.Find("Warning Canvas");
            GameObject title = warningCanvas.transform.Find("Interaction Warning").transform.Find("BG").transform.Find("Title").gameObject;
            TextMeshProUGUI tmProUGUI = title.GetComponent<TextMeshProUGUI>();
            tmProUGUI.text = "<sprite=0> " + text;

        }

        
    }
}
