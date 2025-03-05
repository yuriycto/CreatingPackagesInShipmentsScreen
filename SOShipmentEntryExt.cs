using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    [PXProtectedAccess]
    public abstract class CarrierRatesExt : PXGraphExtension<SOShipmentEntry.CarrierRates, SOShipmentEntry>
    {
        public static bool IsActive() => true;

        [PXProtectedAccess]
        public abstract IList<SOPackageEngine.PackSet> CalculatePackages(SOShipment shipment, out SOPackageEngine.PackSet manualPackSet);
    }

    public class SOShipmentEntryExt : PXGraphExtension<CarrierRatesExt, SOShipmentEntry>
    {
        public static bool IsActive() => true;

        #region Injections
        [InjectDependency]
        public ISODataProvider _soDataProvider { get; set; }
        #endregion

        public SelectFrom<SelectedPackageContents>
            .Where<SelectedPackageContents.shipmentNbr.IsEqual<SOPackageDetailEx.shipmentNbr.FromCurrent>
            .And<SelectedPackageContents.packageLineNbr.IsEqual<SOPackageDetailEx.lineNbr.FromCurrent>>>.OrderBy<SelectedPackageContents.defaultIssueFrom.Asc>.View SelectedPackageContentsView;

        protected void _(Events.FieldVerifying<SOPackageDetailExt.usrIsParentBox> e)
        {
            if (e == null)
            {
                return;
            }
            var args = (SOPackageDetailEx)e.Args.Row;
            CSBox box = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, args.BoxID);
            var canUseLikeParent = box.GetExtension<CSBoxExt>().UsrUseAsParentBox;
            if (canUseLikeParent == null || !(bool)canUseLikeParent)
            {
                throw new PXSetPropertyException("This box cannot be used as Master Pack Carton. Please move to the Boxes screen and mark “Can Be Used as Master Pack Carton” as True.", PXErrorLevel.Error);
            }
            return;
        }

        protected void _(Events.RowSelected<SOPackageDetail> e)
        {
            if (e.Row == null)
            {
                return;
            }
            bool isParentBox = e.Row.GetExtension<SOPackageDetailExt>()?.UsrIsParentBox == true;
            Base.PackageDetailExt.PackageDetailSplit.AllowInsert = !isParentBox;
            Base.PackageDetailExt.PackageDetailSplit.AllowUpdate = !isParentBox;
            SelectedPackageContentsView.AllowInsert = !isParentBox;
            SelectedPackageContentsView.AllowUpdate = !isParentBox;
            PXUIFieldAttribute.SetEnabled<SOPackageDetailExt.usrSelectedParentBox>(Base.Packages.Cache, e.Row, !isParentBox);


        }

        protected void _(Events.FieldUpdated<SOPackageDetailExt.usrIsParentBox> e)
        {
            if (e == null)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                Base.PackageDetailExt.PackageDetailSplit.AllowInsert = false;
                Base.PackageDetailExt.PackageDetailSplit.AllowUpdate = false;
                SelectedPackageContentsView.AllowInsert = false;
                SelectedPackageContentsView.AllowUpdate = false;

                foreach (var item in Base.PackageDetailExt.PackageDetailSplit.Select())
                {
                    Base.PackageDetailExt.PackageDetailSplit.Delete(item);
                }

                foreach (var item in SelectedPackageContentsView.Select())
                {
                    SelectedPackageContentsView.Delete(item);
                }
                PXUIFieldAttribute.SetReadOnly<SOPackageDetailExt.usrSelectedParentBox>(Base.Packages.Cache, e.Row, true);
                return;
            }
            Base.PackageDetailExt.PackageDetailSplit.AllowInsert = true;
            Base.PackageDetailExt.PackageDetailSplit.AllowUpdate = true;
            SelectedPackageContentsView.AllowInsert = true;
            SelectedPackageContentsView.AllowUpdate = true;
            PXUIFieldAttribute.SetReadOnly<SOPackageDetailExt.usrSelectedParentBox>(Base.Packages.Cache, e.Row, false);
        }

        protected void _(Events.FieldVerifying<SOPackageDetailEx.boxID> e)
        {
            if (e == null || !e.ExternalCall)
            {
                return;
            }

            var args = (SOPackageDetailEx)e.Args.Row;
            var customer = Base.Document.Current.CustomerID;
            var boxId = e.NewValue;

            Customer originalCustomer = SelectFrom<Customer>.Where<Customer.bAccountID.IsEqual<@P.AsInt>>.View.Select(Base, customer);


            CustomerPackaging packaging = SelectFrom<CustomerPackaging>.Where<CustomerPackaging.customer.IsEqual<@P.AsInt>>.View.Select(Base, customer);

            CustomerBoxesDAC customerBoxes = SelectFrom<CustomerBoxesDAC>.Where<CustomerBoxesDAC.boxID.IsEqual<@P.AsString>
                .And<CustomerBoxesDAC.customerID.IsEqual<@P.AsInt>>>.View.Select(Base, boxId, originalCustomer.BAccountID);

            if (packaging == null || customerBoxes == null)
            {
                return;
            }

            if (packaging.UseOnlyCustomerBoxes == true && customerBoxes.Active == false)
            {
                throw new PXSetPropertyException("This box is not used for that particular customer. Please, make sure that you selected the correct one or make box active on the Customers screen -> Boxes tab");
            }
        }

        protected void _(Events.RowSelected<SOPackageDetailEx> row)
        {
            var currentRow = row.Row;
            if (currentRow == null) return;

            var package = currentRow.GetExtension<SOPackageDetailExExt>();
            if (package == null) return;

            var packageContent = SelectFrom<SelectedPackageContents>
                .Where<SelectedPackageContents.shipmentNbr.IsEqual<SOPackageDetailEx.shipmentNbr.FromCurrent>
                .And<SelectedPackageContents.packageLineNbr.IsEqual<SOPackageDetailEx.lineNbr.FromCurrent>>>.View
                .Select(Base).FirstTableItems;
            var firstContent = packageContent.FirstOrDefault();
            var firstOrderNbr = firstContent?.OrderNbr;
            var firstStoreNbr = firstContent?.StoreNbr;
            int countOrder = 0;
            int countStore = 0;
            decimal? estimatedTotalQuantity = 0;
            decimal? contentTotalQuantity = 0;
            if (firstOrderNbr != null) countOrder++;
            if (firstStoreNbr != null) countStore++;
            foreach (var item in packageContent)
            {
                var ordernNbr = item?.OrderNbr;
                var storeNbr = item?.StoreNbr;
                if (ordernNbr != firstOrderNbr)
                {
                    countOrder++;
                }
                if (storeNbr != firstStoreNbr)
                {
                    countStore++;
                }
                estimatedTotalQuantity += item.PackedQty;
            }

            var selectedContent = SelectFrom<SOShipLineSplitPackage>
                .Where<SOShipLineSplitPackage.shipmentNbr.IsEqual<SOPackageDetailEx.shipmentNbr.FromCurrent>
                .And<SOShipLineSplitPackage.packageLineNbr.IsEqual<SOPackageDetailEx.lineNbr.FromCurrent>>>.View
                .Select(Base).FirstTableItems;

            foreach (var item in selectedContent)
            {
                contentTotalQuantity += item.PackedQty;
            }

            currentRow.GetExtension<SOPackageDetailExt>().UsrOrderNbr = countOrder > 1 ? "<SPLIT>" : firstOrderNbr;
            currentRow.GetExtension<SOPackageDetailExt>().UsrStoreNbr = countStore > 1 ? "<SPLIT>" : firstStoreNbr;
            currentRow.GetExtension<SOPackageDetailExt>().UsrEstPackageQuantity = estimatedTotalQuantity;
            currentRow.GetExtension<SOPackageDetailExt>().UsrContentPackageQuantity = contentTotalQuantity;
        }

        protected void _(Events.RowUpdated<SOPackageDetailEx> e)
        {
            if (e == null)
            {
                return;
            }

            var oldParentBoxID = e.OldRow.GetExtension<SOPackageDetailExt>().UsrSelectedParentBox;
            var currentParentBox = e.Row.GetExtension<SOPackageDetailExt>().UsrSelectedParentBox;

            var parentPackageID = currentParentBox;

            if (parentPackageID == null && oldParentBoxID == null)
            {
                return;
            }

            if (parentPackageID != null)
            {
                var view = Base.Packages.Select().FirstTableItems;

                var parentPackage = view.FirstOrDefault(x => PXCache<SOPackageDetail>.GetExtension<SOPackageDetailExt>(x).UsrCartonNbr == parentPackageID);

                var childrenBoxes = view.Where(x => x.GetExtension<SOPackageDetailExt>().UsrSelectedParentBox == parentPackageID);

                parentPackage.Weight = 0;
                parentPackage.COD = 0;
                parentPackage.DeclaredValue = 0;

                foreach (var child in childrenBoxes)
                {
                    parentPackage.Weight += child.Weight;
                    parentPackage.COD += child.COD;
                    parentPackage.DeclaredValue += child.DeclaredValue;
                }
                e.Cache.SetValueExt<SOPackageDetailEx.weight>(parentPackage, parentPackage.Weight);
                e.Cache.SetValueExt<SOPackageDetailEx.cOD>(parentPackage, parentPackage.COD);
                e.Cache.SetValueExt<SOPackageDetailEx.declaredValue>(parentPackage, parentPackage.DeclaredValue);
                Base.Packages.Update(parentPackage); //may need to coment

                Base.Packages.View.RequestRefresh();
            }

            if (oldParentBoxID != null)
            {
                var view = Base.Packages.Select().FirstTableItems;
                var parentPackage = view.First(x => PXCache<SOPackageDetail>.GetExtension<SOPackageDetailExt>(x).UsrCartonNbr == oldParentBoxID);

                parentPackage.Weight -= e.Row.Weight;
                parentPackage.COD -= e.Row.COD;
                parentPackage.DeclaredValue -= e.Row.DeclaredValue;
                e.Cache.SetValueExt<SOPackageDetailEx.weight>(parentPackage, parentPackage.Weight);
                e.Cache.SetValueExt<SOPackageDetailEx.cOD>(parentPackage, parentPackage.COD);
                e.Cache.SetValueExt<SOPackageDetailEx.declaredValue>(parentPackage, parentPackage.DeclaredValue);
                Base.Packages.Update(parentPackage); //may need to coment

                Base.Packages.View.RequestRefresh();
            }
        }

        public PXAction<SOPackageDetail> printLabelForPackage;
        [PXUIField(DisplayName = "PRINT LABEL FOR PACKAGE", MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
        [PXLookupButton]
        public virtual IEnumerable PrintLabelForPackage(PXAdapter adapter)
        {
            return adapter.Get();
        }

        public PXAction<SOPackageDetail> printLabelForPackages;
        [PXUIField(DisplayName = "PRINT LABELS FOR ALL PACKAGES", MapEnableRights = PXCacheRights.Select, MapViewRights = PXCacheRights.Select)]
        [PXLookupButton]
        public virtual IEnumerable PrintLabelForPackages(PXAdapter adapter)
        {
            return adapter.Get();
        }

        public delegate void CreateShipmentDelegate(CreateShipmentArgs args);

        [PXOverride]
        public virtual void CreateShipment(CreateShipmentArgs args, CreateShipmentDelegate baseMethod)
        {
            var order = args.Order;

            UpdateShipmentLines(args.Order);

            Customer originalCustomer = SelectFrom<Customer>.Where<Customer.bAccountID.IsEqual<@P.AsInt>>.View.Select(Base, order.CustomerID);

            var customerPackages = SelectFrom<CustomerPackaging>.Where<CustomerPackaging.customer.IsEqual<@P.AsInt>>.View.Select(Base, order.CustomerID).TopFirst;
            var boxes = SelectFrom<CustomerBoxesDAC>
                        .Where<CustomerBoxesDAC.customerID.IsEqual<@P.AsInt>
                        .And<CustomerBoxesDAC.active.IsEqual<True>>>.View
                        .Select(Base, originalCustomer.BAccountID)
                        .RowCast<CustomerBoxesDAC>()
                        .ToList();
            List<CustomerBoxesDAC> customerBoxes = new List<CustomerBoxesDAC>();

            if (customerPackages != null && customerPackages.UseOnlyCustomerBoxes == true && boxes.Count <= 0)
            {
                throw new PXException("Use Customer Boxes feature is enabled for this customer. However boxes are not defined or not activated on the Boxes tab on the Customers screen. Please specify and activate boxes for this customer.");
            }
            if (customerPackages != null && customerPackages.UseOnlyCustomerBoxes == true)
            {
                customerBoxes = SelectFrom<CustomerBoxesDAC>
                        .Where<CustomerBoxesDAC.customerID.IsEqual<@P.AsInt>
                        .And<CustomerBoxesDAC.active.IsEqual<True>>>.View
                        .Select(Base, originalCustomer.BAccountID)
                        .RowCast<CustomerBoxesDAC>()
                        .ToList();
            }

            baseMethod(args);
            var packages = Base.Packages.Select().FirstTableItems.ToList();
            List<SOPackageDetailEx> orderPackages = null;
            foreach (var package in packages)
            {
                if (package.ShipmentNbr == Base.Document.Current.ShipmentNbr && package.PackageType == "A")
                {
                    Base.Packages.Delete(package);
                    Base.Actions.PressSave();
                }
            }


            if (order.GetExtension<SOOrderExt>().UsrPackOrderSeparately == true)
            {
                orderPackages = CreateOrderPackages(order, customerBoxes);
            }

            var shipmentsSplit = SelectFrom<SOShipLineSplit>.Where<SOShipLineSplit.shipmentNbr.IsEqual<SOShipment.shipmentNbr.FromCurrent>.And<SOShipLineSplit.origOrderNbr.IsEqual<@P.AsString>>>.View.Select(Base, order.OrderNbr).RowCast<SOShipLineSplit>().ToList();


            foreach (var shipLine in shipmentsSplit)
            {
                var item = SelectFrom<InventoryItem>.Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>.View
                                .Select(Base, shipLine.InventoryID).TopFirst;
                if (order.GetExtension<SOOrderExt>().UsrPackOrderSeparately == true)
                {
                    switch (item.PackageOption)
                    {
                        case "W":
                            CreatePackagesByWeightSeparately(Base, shipLine, order.OrderNbr, orderPackages);
                            break;
                        case "V":
                            CreatePackagesByVolumeAndWeightSeparately(Base, shipLine, order.OrderNbr, orderPackages);
                            break;
                        default:
                            break;
                    }
                }

                if (order.GetExtension<SOOrderExt>().UsrPackOrderSeparately == false)
                {
                    switch (item.PackageOption)
                    {
                        case "W":
                            CreatePackagesByWeight(Base, shipLine, customerBoxes);
                            break;
                        case "V":
                            CreatePackagesByVolumeAndWeight(Base, shipLine, customerBoxes);
                            break;
                        default:
                            break;
                    }
                }
            }
        }


        public delegate IEnumerable CreateInvoiceDelegate(PXAdapter adapter);

        [PXOverride]
        public virtual IEnumerable CreateInvoice(PXAdapter adapter, CreateInvoiceDelegate baseMethod)
        {
            //baseMethod(adapter);
            var transactions = Base.Transactions.Select().FirstTableItems;
            var order = SelectFrom<SOOrder>.Where<SOOrder.orderNbr.IsEqual<@P.AsString>>.View.Select(Base, transactions.First().OrigOrderNbr).TopFirst;

            var customerOrdernNumber = order.CustomerOrderNbr;
            foreach (var transaction in transactions)
            {
                var tenpOrder = SelectFrom<SOOrder>.Where<SOOrder.orderNbr.IsEqual<@P.AsString>>.View.Select(Base, transaction.OrigOrderNbr).TopFirst;
                if (tenpOrder.CustomerOrderNbr != customerOrdernNumber)
                {
                    customerOrdernNumber = string.Empty;
                }
            }

            List<SOShipment> shipments = adapter.Get<SOShipment>().ToList();
            (bool MassProcess, bool AllowRedirect, PXQuickProcess.ActionFlow QuickProcessFlow) adapterSlice = (adapter.MassProcess, adapter.AllowRedirect, adapter.QuickProcessFlow);
            bool redirectRequired = !Base.IsImport;
            if (!adapter.Arguments.TryGetValue("InvoiceDate", out var invoiceDate) || invoiceDate == null)
            {
                invoiceDate = Base.Accessinfo.BusinessDate;
            }
            Base.Save.Press();
            PXLongOperation.StartOperation(this, delegate
            {
                SOShipmentEntry sOShipmentEntry = PXGraph.CreateInstance<SOShipmentEntry>();
                SOInvoiceEntry sOInvoiceEntry = PXGraph.CreateInstance<SOInvoiceEntry>();
                InvoiceList invoiceList = new ShipmentInvoices(sOShipmentEntry);
                foreach (SOShipment item in shipments)
                {
                    try
                    {
                        sOShipmentEntry.SelectTimeStamp();
                        sOInvoiceEntry.SelectTimeStamp();
                        if (adapterSlice.MassProcess)
                        {
                            PXProcessing<SOShipment>.SetCurrentItem(item);
                        }
                        sOShipmentEntry.InvoiceShipment(sOInvoiceEntry, item, (DateTime)invoiceDate, invoiceList, adapterSlice.QuickProcessFlow);
                        if (adapterSlice.MassProcess)
                        {
                            sOShipmentEntry.Document.Cache.RestoreCopy(item, PrimaryKeyOf<SOShipment>.By<SOShipment.shipmentNbr>.Find(sOShipmentEntry, item));
                        }
                    }
                    catch (Exception error) when (adapterSlice.MassProcess)
                    {
                        PXProcessing<SOShipment>.SetError(error);
                    }
                }
                if (adapterSlice.AllowRedirect && !adapterSlice.MassProcess && redirectRequired && invoiceList.Count > 0)
                {
                    using (new PXTimeStampScope(null))
                    {
                        ARInvoice aRInvoice = invoiceList[0];
                        sOInvoiceEntry = PXGraph.CreateInstance<SOInvoiceEntry>();
                        sOInvoiceEntry.Document.Current = sOInvoiceEntry.Document.Search<ARInvoice.docType, ARInvoice.refNbr>(aRInvoice.DocType, aRInvoice.RefNbr, new object[1] { aRInvoice.DocType });
                        sOInvoiceEntry.Document.Current.InvoiceNbr = customerOrdernNumber;
                        throw new PXRedirectRequiredException(sOInvoiceEntry, "Invoice");
                    }
                }
            });
            return shipments;
        }

        protected List<SOPackageDetailEx> CreateOrderPackages(SOOrder order, List<CustomerBoxesDAC> customerBoxes)
        {

            var orderLines = SelectFrom<SOLine>
                                .Where<SOLine.orderNbr.IsEqual<@P.AsString>>
                                .View.Select(Base, order.OrderNbr)
                                .FirstTableItems.ToList();

            List<CSBox> boxes = new List<CSBox>();


            foreach (var customerBox in customerBoxes)
            {
                var box = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, customerBox.BoxID).TopFirst;

                if (box != null)
                {
                    boxes.Add(box);
                }
            }


            var orderQty = GetOrderQty(orderLines);

            decimal volume = 0;

            List<SOPackageDetailEx> orderPackages = new List<SOPackageDetailEx>();

            foreach (var orderLine in orderLines)
            {
                var itemWeight = GetItemWeight(orderLine.InventoryID);
                var itemVolume = GetItemVolume(orderLine.InventoryID);


                var InItemBoxs = SelectFrom<INItemBoxEx>
                                    .Where<INItemBoxEx.inventoryID.IsEqual<@P.AsInt>>
                                    .View.Select(Base, orderLine.InventoryID)
                                    .RowCast<INItemBoxEx>().ToList();

                if (customerBoxes == null || customerBoxes.Count <= 0)
                {
                    foreach (var InItemBox in InItemBoxs)
                    {
                        var box = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, InItemBox.BoxID).TopFirst;

                        if (box != null)
                        {
                            boxes.Add(box);
                        }
                    }
                }

                InventoryItem item = SelectFrom<InventoryItem>.Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>.View
                                    .Select(Base, orderLine.InventoryID).TopFirst;

                CSBox selectedBox = null;

                switch (item.PackageOption)
                {
                    case "W":
                        selectedBox = boxes.OrderByDescending(box => box.MaxWeight).FirstOrDefault();
                        foreach (var box in boxes.OrderBy(box => box.MaxWeight))
                        {
                            if (box.MaxWeight / itemWeight >= orderQty)
                            {
                                selectedBox = box;
                                break;
                            }
                        }
                        break;
                    case "V":
                        selectedBox = boxes.OrderByDescending(box => box.MaxWeight)
                                                            .ThenByDescending(box => box.MaxVolume)
                                                            .FirstOrDefault();
                        foreach (var box in boxes.OrderBy(box => box.MaxWeight).ThenBy(box => box.MaxVolume))
                        {
                            var maxQtyByWeight = (int)(box.MaxWeight / itemWeight);
                            var maxQtyByVolume = (int)(box.MaxVolume / itemVolume);

                            int maxQtyFit = Math.Min(maxQtyByWeight, maxQtyByVolume);

                            if (maxQtyFit >= orderQty)
                            {
                                selectedBox = box;
                                break;
                            }
                        }
                        break;
                    default:
                        break;
                }

                var optimalBoxWeight = InItemBoxs.OrderByDescending(box => box.MaxWeight).FirstOrDefault();
                var optimalBoxVolumeAndWeight = InItemBoxs.OrderByDescending(box => box.MaxWeight)
                                                            .ThenByDescending(box => box.MaxVolume)
                                                            .FirstOrDefault();

                if (optimalBoxWeight == null || optimalBoxVolumeAndWeight == null)
                    continue;


                while (orderLine.Qty > 0)
                {

                    var currentPackage = orderPackages.LastOrDefault();

                    int itemsToPack = 0;


                    if (currentPackage == null)
                    {
                        currentPackage = new SOPackageDetailEx
                        {
                            Confirmed = false,
                            BoxID = selectedBox.BoxID,
                            Weight = 0,
                        };
                        currentPackage = Base.Packages.Insert(currentPackage);
                        Base.Actions.PressSave();
                        orderPackages.Add(currentPackage);
                        volume = 0;
                    }


                    if (item.PackageOption == "W")
                    {
                        var remainingWeight = (decimal)currentPackage.MaxWeight - currentPackage.Weight;
                        var maxQtyByWeight = (int)(remainingWeight / itemWeight);

                        decimal remainingCapacity = 0;

                        remainingCapacity = (decimal)(currentPackage.MaxWeight - currentPackage.Weight);

                        if (remainingCapacity < itemWeight)
                        {
                            selectedBox = boxes.OrderByDescending(box => box.MaxWeight).FirstOrDefault();

                            foreach (var box in boxes.OrderBy(box => box.MaxWeight))
                            {
                                if (box.MaxWeight / itemWeight >= orderQty)
                                {
                                    selectedBox = box;
                                    break;
                                }
                            }

                            currentPackage = new SOPackageDetailEx
                            {
                                Confirmed = false,
                                BoxID = selectedBox.BoxID,
                                Weight = 0,
                            };

                            currentPackage = Base.Packages.Insert(currentPackage);
                            Base.Actions.PressSave();
                            orderPackages.Add(currentPackage);
                            volume = 0;
                            remainingCapacity = (decimal)(currentPackage.MaxWeight - currentPackage.Weight);
                        }
                        itemsToPack = (int)Math.Floor(Math.Min((decimal)orderLine.Qty, (decimal)(currentPackage.MaxWeight / itemWeight)));
                        currentPackage.Weight += itemsToPack * itemWeight;
                        volume += itemsToPack * itemVolume;
                    }

                    if (item.PackageOption == "V")
                    {
                        var itemBox = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, currentPackage.BoxID).TopFirst;
                        var remainingVolume = (decimal)itemBox.MaxVolume - volume;
                        var remainingWeight = (decimal)currentPackage.MaxWeight - currentPackage.Weight;


                        var maxQtyByVolume = (int)(remainingVolume / itemVolume);
                        var maxQtyByWeight = (int)(remainingWeight / itemWeight);

                        decimal remainingCapacity = 0;

                        if (maxQtyByVolume < maxQtyByWeight)
                        {
                            itemBox = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, currentPackage.BoxID).TopFirst;
                            remainingCapacity = (decimal)itemBox.MaxVolume - volume;
                            if (remainingCapacity < itemVolume)
                            {
                                selectedBox = boxes.OrderByDescending(box => box.MaxWeight)
                                                                .ThenByDescending(box => box.MaxVolume)
                                                                .FirstOrDefault();

                                foreach (var box in boxes.OrderBy(box => box.MaxWeight).ThenBy(box => box.MaxVolume))
                                {
                                    var maxQtyByWeightNew = (int)(box.MaxWeight / itemWeight);
                                    var maxQtyByVolumeNew = (int)(box.MaxVolume / itemVolume);

                                    int maxQtyFit = Math.Min(maxQtyByWeightNew, maxQtyByVolumeNew);

                                    if (maxQtyFit >= orderQty)
                                    {
                                        selectedBox = box;
                                        break;
                                    }
                                }

                                currentPackage = new SOPackageDetailEx
                                {
                                    Confirmed = false,
                                    BoxID = selectedBox.BoxID,
                                    Weight = 0,
                                };
                                currentPackage = Base.Packages.Insert(currentPackage);
                                Base.Actions.PressSave();
                                orderPackages.Add(currentPackage);
                                volume = 0;
                                itemBox = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, currentPackage.BoxID).TopFirst;
                                remainingCapacity = (decimal)itemBox.MaxVolume - volume;
                            }
                            itemsToPack = Math.Min((int)orderLine.Qty, (int)(remainingCapacity / itemVolume));
                        }
                        if (maxQtyByVolume >= maxQtyByWeight)
                        {
                            remainingCapacity = (decimal)(currentPackage.MaxWeight - currentPackage.Weight);
                            if (remainingCapacity < itemWeight)
                            {
                                selectedBox = boxes.OrderByDescending(box => box.MaxWeight).FirstOrDefault();

                                foreach (var box in boxes.OrderBy(box => box.MaxWeight))
                                {
                                    if (box.MaxWeight / itemWeight >= orderQty)
                                    {
                                        selectedBox = box;
                                        break;
                                    }
                                }

                                currentPackage = new SOPackageDetailEx
                                {
                                    Confirmed = false,
                                    BoxID = selectedBox.BoxID,
                                    Weight = 0,
                                };
                                currentPackage = Base.Packages.Insert(currentPackage);
                                Base.Actions.PressSave();
                                orderPackages.Add(currentPackage);
                                volume = 0;
                                remainingCapacity = (decimal)(currentPackage.MaxWeight - currentPackage.Weight);
                            }
                            itemsToPack = (int)Math.Floor(Math.Min((decimal)orderLine.Qty, (decimal)(currentPackage.MaxWeight / itemWeight)));
                        }

                        currentPackage.Weight += itemsToPack * itemWeight;
                        volume += itemsToPack * itemVolume;
                    }

                    orderLine.Qty -= itemsToPack;
                    orderQty -= itemsToPack;

                    if (currentPackage.Weight >= optimalBoxWeight.MaxWeight || volume >= optimalBoxVolumeAndWeight.MaxVolume)
                    {
                        currentPackage = null;
                    }
                }
            }

            return orderPackages;
        }

        protected decimal GetOrderWeight(List<SOLine> orderLines)
        {
            return (decimal)orderLines.Sum(orderLine => GetItemWeight(orderLine.InventoryID) * orderLine.Qty);
        }

        protected decimal GetOrderVolume(List<SOLine> orderLines)
        {
            return (decimal)orderLines.Sum(orderLine => GetItemVolume(orderLine.InventoryID) * orderLine.Qty);
        }

        protected int GetOrderQty(List<SOLine> orderLines)
        {
            return (int)orderLines.Sum(orderLine => orderLine.Qty);
        }

        protected decimal GetItemWeight(int? inventoryID)
        {
            InventoryItem item = SelectFrom<InventoryItem>.Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>.View
                                    .Select(Base, inventoryID).TopFirst;
            return item?.BaseItemWeight ?? 0;
        }

        protected decimal GetItemVolume(int? inventoryID)
        {
            InventoryItem item = SelectFrom<InventoryItem>.Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>.View
                                    .Select(Base, inventoryID).TopFirst;
            return item?.BaseItemVolume ?? 0;
        }

        protected decimal GetTotalPackageWeight(SOPackageDetailEx package)
        {
            decimal totalWeight = 0;

            var packageContents = SelectFrom<SelectedPackageContents>
                            .Where<SelectedPackageContents.shipmentNbr.IsEqual<@P.AsString>
                                .And<SelectedPackageContents.packageLineNbr.IsEqual<@P.AsInt>>>
                            .View.Select(Base, package.ShipmentNbr, package.LineNbr).FirstTableItems.ToList();

            foreach (var packageContent in packageContents)
            {
                totalWeight += GetItemWeight(packageContent.InventoryID) * (packageContent.PackedQty ?? 0);
            }

            return totalWeight;
        }

        protected decimal GetTotalPackageVolume(SOPackageDetailEx package)
        {
            decimal totalVolume = 0;

            var packageContents = SelectFrom<SelectedPackageContents>
                            .Where<SelectedPackageContents.shipmentNbr.IsEqual<@P.AsString>
                                .And<SelectedPackageContents.packageLineNbr.IsEqual<@P.AsInt>>>
                            .View.Select(Base, package.ShipmentNbr, package.LineNbr).FirstTableItems.ToList();

            foreach (var packageContent in packageContents)
            {
                totalVolume += GetItemVolume(packageContent.InventoryID) * (packageContent.PackedQty ?? 0);
            }

            return totalVolume;
        }


        protected void CreatePackagesByWeightSeparately(SOShipmentEntry sOShipmentEntry, SOShipLineSplit soShipLine, string orderNbr, List<SOPackageDetailEx> packages)
        {
            decimal itemWeight = GetItemWeight(soShipLine.InventoryID);
            decimal qty = soShipLine.Qty ?? 0m;
            var shipLine = SelectFrom<SOShipLine>.Where<SOShipLine.origOrderNbr.IsEqual<@P.AsString>.And<SOShipLine.inventoryID.IsEqual<@P.AsInt>>>.View.Select(sOShipmentEntry, orderNbr, soShipLine.InventoryID).TopFirst;
            var order = SelectFrom<SOOrder>.Where<SOOrder.orderNbr.IsEqual<@P.AsString>>.View.Select(sOShipmentEntry, orderNbr).TopFirst;
            var shipment = Base.CurrentDocument.Current;
            var storeNbr = order.GetExtension<SOOrderExt>()?.UsrTCStoreNumber;
            var itemWarehouseDetails = SelectFrom<InventoryItem>
                .Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>
                .View.Select(Base, shipLine.InventoryID).TopFirst;

            foreach (var package in packages)
            {
                package.GetExtension<SOPackageDetailExt>().UsrSepareteOrderNbr = orderNbr;
                decimal remainingCapacity = (decimal)package.MaxWeight - GetTotalPackageWeight(package);

                if (remainingCapacity < itemWeight)
                {
                    continue;
                }

                int itemsToPack = (int)Math.Min(qty, Math.Floor(remainingCapacity / itemWeight));

                package.Weight += itemsToPack * itemWeight;

                var newPackageContent = new SelectedPackageContents
                {
                    ShipmentNbr = package.ShipmentNbr,
                    PackageLineNbr = package.LineNbr,
                    OrderNbr = shipLine.OrigOrderNbr,
                    StoreNbr = storeNbr,
                    InventoryID = soShipLine.InventoryID,
                    PackedQty = itemsToPack,
                    ShipmentSplitLineNbr = soShipLine.SplitLineNbr,
                    DefaultIssueFrom = itemWarehouseDetails.DfltShipLocationID
                };

                SelectedPackageContentsView.Insert(newPackageContent);

                Base.Actions.PressSave();
                qty -= itemsToPack;

                if (qty == 0)
                {
                    break;
                }
            }
        }

        protected void CreatePackagesByVolumeAndWeightSeparately(SOShipmentEntry sOShipmentEntry, SOShipLineSplit soShipLine, string orderNbr, List<SOPackageDetailEx> packages)
        {
            decimal itemWeight = GetItemWeight(soShipLine.InventoryID);
            decimal itemVolume = GetItemVolume(soShipLine.InventoryID);
            decimal qty = soShipLine.Qty ?? 0m;

            var shipLine = SelectFrom<SOShipLine>.Where<SOShipLine.origOrderNbr.IsEqual<@P.AsString>.And<SOShipLine.inventoryID.IsEqual<@P.AsInt>>>.View.Select(sOShipmentEntry, orderNbr, soShipLine.InventoryID).TopFirst;
            var order = SelectFrom<SOOrder>.Where<SOOrder.orderNbr.IsEqual<@P.AsString>>.View.Select(sOShipmentEntry, orderNbr).TopFirst;

            var shipment = Base.CurrentDocument.Current;
            var storeNbr = order.GetExtension<SOOrderExt>()?.UsrTCStoreNumber;

            InventoryItem item = SelectFrom<InventoryItem>.Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>.View
                                    .Select(Base, soShipLine.InventoryID).TopFirst;
            var itemWarehouseDetails = SelectFrom<InventoryItem>
                .Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>
                .View.Select(Base, shipLine.InventoryID).TopFirst;

            foreach (var package in packages)
            {
                package.GetExtension<SOPackageDetailExt>().UsrSepareteOrderNbr = orderNbr;
                var itemBox = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, package.BoxID).TopFirst;

                decimal remainingCapacityWeight = (decimal)package.MaxWeight - GetTotalPackageWeight(package);
                decimal remainingCapacityVolume = (decimal)itemBox.MaxVolume - GetTotalPackageVolume(package);

                if (remainingCapacityWeight < itemWeight || remainingCapacityVolume < itemVolume)
                {
                    continue;
                }

                int maxItemsByWeight = (int)Math.Floor(remainingCapacityWeight / itemWeight);
                int maxItemsByVolume = (int)Math.Floor(remainingCapacityVolume / itemVolume);

                int itemsToPack = (int)Math.Min(qty, Math.Min(maxItemsByWeight, maxItemsByVolume));

                package.Weight += itemsToPack * itemWeight;

                var newPackageContent = new SelectedPackageContents
                {
                    ShipmentNbr = package.ShipmentNbr,
                    PackageLineNbr = package.LineNbr,
                    OrderNbr = shipLine.OrigOrderNbr,
                    StoreNbr = storeNbr,
                    InventoryID = soShipLine.InventoryID,
                    PackedQty = itemsToPack,
                    ShipmentSplitLineNbr = soShipLine.SplitLineNbr,
                    DefaultIssueFrom = itemWarehouseDetails.DfltShipLocationID
                };

                SelectedPackageContentsView.Insert(newPackageContent);

                Base.Actions.PressSave();

                qty -= itemsToPack;

                if (qty == 0)
                {
                    break;
                }
            }
        }

        protected void CreatePackagesByWeight(SOShipmentEntry sOShipmentEntry, SOShipLineSplit order, List<CustomerBoxesDAC> customerBoxes)
        {
            List<CSBox> boxes = new List<CSBox>();

            var shipLine = SelectFrom<SOShipLine>.Where<SOShipLine.origOrderNbr.IsEqual<@P.AsString>.And<SOShipLine.inventoryID.IsEqual<@P.AsInt>>>.View.Select(sOShipmentEntry, order.OrigOrderNbr, order.InventoryID).TopFirst;

            foreach (var customerBox in customerBoxes)
            {
                var box = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, customerBox.BoxID).TopFirst;

                if (box != null)
                {
                    boxes.Add(box);
                }
            }

            var lastPackage = Base.Packages.Select().FirstTableItems?.LastOrDefault();
            var InItemBoxs = SelectFrom<INItemBoxEx>.Where<INItemBoxEx.inventoryID.IsEqual<@P.AsInt>>.View.Select(Base, order.InventoryID).RowCast<INItemBoxEx>().ToList();
            decimal itemWeight = GetItemWeight(order.InventoryID);
            var qty = order.Qty;

            if (customerBoxes == null || customerBoxes.Count <= 0)
            {
                foreach (var inItemBox in InItemBoxs)
                {
                    var box = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, inItemBox.BoxID).TopFirst;
                    boxes.Add(box);
                }
            }

            var currentBox = boxes.OrderByDescending(b => b.MaxWeight)
                      .FirstOrDefault();


            var shipmentsSplit = SelectFrom<SOShipLineSplit>.Where<SOShipLineSplit.shipmentNbr.IsEqual<SOShipment.shipmentNbr.FromCurrent>>.View.Select(Base).FirstTableItems.ToList();

            var shipment = Base.CurrentDocument.Current;
            var currentOrder = SelectFrom<SOOrder>.Where<SOOrder.orderNbr.IsEqual<@P.AsString>>.View.Select(sOShipmentEntry, order.OrigOrderNbr).TopFirst;

            var itemWarehouseDetails = SelectFrom<InventoryItem>
                .Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>
                .View.Select(Base, shipLine.InventoryID).TopFirst;
            var storeNbr = currentOrder.GetExtension<SOOrderExt>()?.UsrTCStoreNumber;

            while (qty > 0)
            {
                foreach (var box in boxes.OrderBy(box => box.MaxWeight))
                {
                    if (box.MaxWeight / itemWeight >= qty)
                    {
                        currentBox = box;
                        break;
                    }
                }

                SOPackageDetailEx package = null;
                if (lastPackage != null && lastPackage.GetExtension<SOPackageDetailExt>().UsrSepareteOrderNbr == null)
                {
                    package = lastPackage;
                }
                else
                {
                    package = new SOPackageDetailEx
                    {
                        Confirmed = false,
                        BoxID = currentBox.BoxID,
                    };
                    package = Base.Packages.Insert(package);
                }

                decimal remainingWeightCapacity = (decimal)(package.MaxWeight - GetTotalPackageWeight(package));
                if (remainingWeightCapacity < itemWeight)
                {
                    package = new SOPackageDetailEx
                    {
                        Confirmed = false,
                        BoxID = currentBox.BoxID,
                    };
                    package = Base.Packages.Insert(package);
                }

                Base.Actions.PressSave();

                int itemsToPack = (int)Math.Floor(Math.Min((decimal)qty, (decimal)(package.MaxWeight / itemWeight)));

                var newPackageContent = new SelectedPackageContents
                {
                    ShipmentNbr = package.ShipmentNbr,
                    PackageLineNbr = package.LineNbr,
                    OrderNbr = shipLine.OrigOrderNbr,
                    StoreNbr = storeNbr,
                    InventoryID = order.InventoryID,
                    PackedQty = itemsToPack,
                    ShipmentSplitLineNbr = order.SplitLineNbr,
                    DefaultIssueFrom = itemWarehouseDetails.DfltShipLocationID
                };

                SelectedPackageContentsView.Insert(newPackageContent);

                Base.Actions.PressSave();

                qty -= itemsToPack;

                if (qty == 0)
                    break;
            }
        }

        protected void CreatePackagesByVolumeAndWeight(SOShipmentEntry sOShipmentEntry, SOShipLineSplit order, List<CustomerBoxesDAC> customerBoxes)
        {

            var shipLine = SelectFrom<SOShipLine>.Where<SOShipLine.origOrderNbr.IsEqual<@P.AsString>.And<SOShipLine.inventoryID.IsEqual<@P.AsInt>>>.View.Select(sOShipmentEntry, order.OrigOrderNbr, order.InventoryID).TopFirst;
            var shipment = Base.CurrentDocument.Current;
            var currentOrder = SelectFrom<SOOrder>.Where<SOOrder.orderNbr.IsEqual<@P.AsString>>.View.Select(sOShipmentEntry, order.OrigOrderNbr).TopFirst;

            var storeNbr = currentOrder.GetExtension<SOOrderExt>()?.UsrTCStoreNumber;

            var itemWarehouseDetails = SelectFrom<InventoryItem>
                .Where<InventoryItem.inventoryID.IsEqual<@P.AsInt>>
                .View.Select(Base, shipLine.InventoryID).TopFirst;

            List<CSBox> boxes = new List<CSBox>();

            foreach (var customerBox in customerBoxes)
            {
                var box = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, customerBox.BoxID).TopFirst;

                if (box != null)
                {
                    boxes.Add(box);
                }
            }


            var lastPackage = Base.Packages.Select().FirstTableItems?.LastOrDefault();
            decimal itemVolume = GetItemVolume(order.InventoryID);
            decimal itemWeight = GetItemWeight(order.InventoryID);
            var InItemBoxs = SelectFrom<INItemBoxEx>.Where<INItemBoxEx.inventoryID.IsEqual<@P.AsInt>>.View.Select(Base, order.InventoryID).RowCast<INItemBoxEx>().ToList();
            decimal qty = order.Qty ?? 0m;

            if (customerBoxes == null || customerBoxes.Count <= 0)
            {
                foreach (var inItemBox in InItemBoxs)
                {
                    var box = SelectFrom<CSBox>.Where<CSBox.boxID.IsEqual<@P.AsString>>.View.Select(Base, inItemBox.BoxID).TopFirst;
                    boxes.Add(box);
                }
            }


            var currentBox = boxes.OrderByDescending(b => b.MaxWeight)
                        .ThenBy(b => b.MaxVolume)
                        .FirstOrDefault();

            while (qty > 0)
            {
                int maxQtyByWeight = 0;
                int maxQtyByVolume = 0;

                foreach (var box in boxes.OrderBy(box => box.MaxWeight).ThenBy(box => box.MaxVolume))
                {
                    maxQtyByWeight = (int)(box.MaxWeight / itemWeight);
                    maxQtyByVolume = (int)(box.MaxVolume / itemVolume);

                    int maxQtyFit = Math.Min(maxQtyByWeight, maxQtyByVolume);

                    if (maxQtyFit >= qty)
                    {
                        currentBox = box;
                        break;
                    }
                }

                SOPackageDetailEx package = lastPackage != null && lastPackage.GetExtension<SOPackageDetailExt>().UsrSepareteOrderNbr == null
                                    ? lastPackage
                                    : Base.Packages.Insert(new SOPackageDetailEx { BoxID = currentBox.BoxID });

                decimal remainingCapacity;

                if (maxQtyByVolume <= maxQtyByWeight)
                {
                    var CrBox = SelectFrom<CSBox>
                                .Where<CSBox.boxID.IsEqual<@P.AsString>>
                                .View.Select(Base, package.BoxID)
                                .TopFirst;
                    remainingCapacity = (decimal)CrBox.MaxVolume - GetTotalPackageVolume(package);
                    if (remainingCapacity < itemVolume)
                    {
                        package = Base.Packages.Insert(new SOPackageDetailEx { BoxID = currentBox.BoxID });
                        remainingCapacity = (decimal)CrBox.MaxVolume - GetTotalPackageVolume(package);

                    }
                }
                else
                {
                    remainingCapacity = (decimal)package.MaxWeight - GetTotalPackageWeight(package);
                    if (remainingCapacity < itemWeight)
                    {
                        package = Base.Packages.Insert(new SOPackageDetailEx { BoxID = currentBox.BoxID });
                        remainingCapacity = (decimal)package.MaxWeight - GetTotalPackageWeight(package);
                    }

                }

                int itemsToPack = (int)Math.Floor(Math.Min(qty, remainingCapacity / (maxQtyByVolume <= maxQtyByWeight ? itemVolume : itemWeight)));

                var newPackageContent = new SelectedPackageContents
                {
                    ShipmentNbr = package.ShipmentNbr,
                    PackageLineNbr = package.LineNbr,
                    OrderNbr = shipLine.OrigOrderNbr,
                    StoreNbr = storeNbr,
                    InventoryID = order.InventoryID,
                    PackedQty = itemsToPack,
                    ShipmentSplitLineNbr = order.SplitLineNbr,
                    DefaultIssueFrom = itemWarehouseDetails.DfltShipLocationID
                };

                SelectedPackageContentsView.Insert(newPackageContent);

                Base.Actions.PressSave();

                qty -= itemsToPack;

                if (qty == 0)
                    break;
            }
        }
    }
}
