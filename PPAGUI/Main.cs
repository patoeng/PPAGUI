using ComponentFactory.Krypton.Toolkit;
using MesData;
using MesData.Login;
using MesData.Ppa;
using OpcenterWikLibrary;
using PPAGUI.Enumeration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PPAGUI
{
    public partial class Main : KryptonForm
    {
        #region CONSTRUCTOR
        public Main()
        {
            InitializeComponent();

#if MiniMe
            var  name = "PCBA & Pump Assy Minime";
            Text = Mes.AddVersionNumber(Text + " MiniMe");
#elif Ariel
            var name = "PCBA & Pump Assy Ariel";
            Text = Mes.AddVersionNumber(name);
#endif
            _mesData = new Mes(null, AppSettings.Resource, name);

            WindowState = FormWindowState.Normal;
            Size = new Size(1134, 701);
            lbTitle.Text = AppSettings.Resource;

            _pcbaDataConfig = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
            _pcbaDataConfig?.SaveToFile();

            _pumpDataConfig = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
            _pumpDataConfig?.SaveToFile();

            kryptonNavigator1.SelectedIndex = 0;
            EventLogUtil.LogEvent("Application Start");
        }

        public sealed override string Text
        {
            get => base.Text;
            set => base.Text = value;
        }

        #endregion

        #region INSTANCE VARIABLE

        private PPAState _ppaState;
        private readonly Mes _mesData;
        private DateTime _dMoveIn;
        private PcbaData _pcbaData;
        private PumpData _pumpData;
        private PcbaDataPointConfig _pcbaDataConfig;
        private PumpDataPointConfig _pumpDataConfig;
        private PcbaDataPointConfig _tempPcba;
        private PumpDataPointConfig _tempPump;

        #endregion

        #region FUNCTION USEFULL

        private async Task SetPpaState(PPAState newPpaState)
        {
            _ppaState = newPpaState;
            switch (_ppaState)
            {
                case PPAState.PlaceUnit:
                    _readScanner = false;
                    lblCommand.ForeColor = Color.Red;
                    lblCommand.Text = "Resource is not in \"Up\" condition!";
                    break;
                case PPAState.ScanUnitSerialNumber:
                    ClrContainer();

                    _pcbaData = new PcbaData();
                    _pumpData = new PumpData();

                    if (_mesData.ResourceStatusDetails == null || _mesData.ResourceStatusDetails?.Availability != "Up")
                    {
                        await SetPpaState(PPAState.PlaceUnit);
                        break;
                    }

                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    lblCommand.ForeColor = Color.LimeGreen;
                    lblCommand.Text = @"Scan Unit Serial Number!";
                    ActiveControl = Tb_Scanner;
                    break;
                case PPAState.CheckUnitStatus:
                    _readScanner = false;
                    lblCommand.Text = @"Checking Unit Status";
                    var oContainerStatus = await Mes.GetContainerStatusDetails(_mesData, Tb_SerialNumber.Text, "");
                    if (oContainerStatus != null)
                    {

                        if (oContainerStatus.Operation != null)
                        {

                            if (oContainerStatus.Operation.Name != _mesData.OperationName)
                            {
                                await SetPpaState(PPAState.WrongOperation);
                                break;
                            }
                        }
                        _dMoveIn = DateTime.Now;
                        lbMoveIn.Text = _dMoveIn.ToString(Mes.DateTimeStringFormat);
                        lbMoveOut.Text = "";
                        if (oContainerStatus.MfgOrderName != null && _mesData.ManufacturingOrder == null)
                        {
                            var mfg = await Mes.GetMfgOrder(_mesData, oContainerStatus.MfgOrderName.ToString());
                            _mesData.SetManufacturingOrder(mfg);
                            Tb_PO.Text = oContainerStatus.MfgOrderName.ToString();
                            Tb_Product.Text = oContainerStatus.Product.Name;
                            Tb_ProductDesc.Text = oContainerStatus.ProductDescription.Value;
                            var img = await Mes.GetImage(_mesData, oContainerStatus.Product.Name);
                            pictureBox1.ImageLocation = img.Identifier.Value;

                            var cnt = await Mes.GetCounterFromMfgOrder(_mesData);
                            Tb_PpaQty.Text = cnt.ToString();
                        }

                        if (_mesData.ManufacturingOrder != null &&
                            _mesData.ManufacturingOrder.Name.Value != oContainerStatus.MfgOrderName)
                        {
                            await SetPpaState(PPAState.WrongProductionOrder);
                            break;
                        }

                        if (_pcbaDataConfig.Enable == EnableDisable.Enable &&
                            _pumpDataConfig.Enable == EnableDisable.Enable)
                        {
                            await SetPpaState(PPAState.ScanPcbaOrPumpSerialNumber);
                            break;
                        }

                        if (_pcbaDataConfig.Enable == EnableDisable.Disable &&
                            _pumpDataConfig.Enable == EnableDisable.Disable)
                        {
                            await SetPpaState(PPAState.UpdateMoveInMove);
                            break;
                        }
                        if (_pcbaDataConfig.Enable == EnableDisable.Disable)
                        {
                            await SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        }

                        if (_pumpDataConfig.Enable == EnableDisable.Disable)
                        {
                            await SetPpaState(PPAState.ScanPcbaSerialNumber);
                            break;
                        }
                       
                    }
                    await SetPpaState(PPAState.UnitNotFound);
                    break;
                case PPAState.UnitNotFound:
                    _readScanner = false;
                    lblCommand.ForeColor = Color.Red;
                    lblCommand.Text = "Unit Not Found";
                    break;
                case PPAState.ScanPcbaSerialNumber:
                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    lblCommand.Text = "Scan PCBA Serial Number!";
                    break;
                case PPAState.ScanPumpSerialNumber:
                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    lblCommand.Text = "Scan Pump Serial Number!";
                    break;
                case PPAState.ScanPcbaOrPumpSerialNumber:
                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    lblCommand.Text = @"Scan Pump Or PCBA Serial Number!";
                    break;
                case PPAState.Done:
                    break;
                case PPAState.UpdateMoveInMove:
                    _readScanner = false;

                    /*Move In, Move*/
                    try
                    {
                        oContainerStatus = await Mes.GetContainerStatusDetails(_mesData, Tb_SerialNumber.Text);
                        if (oContainerStatus.ContainerName != null)
                        {
                            lblCommand.Text = @"Container Move In Attempt 1";
                            var transaction = await Mes.ExecuteMoveIn(_mesData, oContainerStatus.ContainerName.Value, _dMoveIn);
                            var resultMoveIn = transaction.Result || transaction.Message == "Move-in has already been performed for this operation.";
                            if (!resultMoveIn && transaction.Message.Contains("TimeOut"))
                            {
                                lblCommand.Text = @"Container Move In Attempt 2";
                                transaction = await Mes.ExecuteMoveIn(_mesData, oContainerStatus.ContainerName.Value, _dMoveIn);
                                resultMoveIn = transaction.Result || transaction.Message == "Move-in has already been performed for this operation.";
                                if (!resultMoveIn && transaction.Message.Contains("TimeOut"))
                                {
                                    lblCommand.Text = @"Container Move In Attempt 3";
                                    transaction = await Mes.ExecuteMoveIn(_mesData, oContainerStatus.ContainerName.Value, _dMoveIn);
                                    resultMoveIn = transaction.Result || transaction.Message == "Move-in has already been performed for this operation.";
                                }
                            }
                            if (resultMoveIn)
                            {
                                //Component Consume
                                var listIssue = new List<dynamic>();
                                if (_pumpDataConfig.Enable == EnableDisable.Enable) listIssue.Add(_pumpData.ToIssueActualDetail());
                                if (_pcbaDataConfig.Enable == EnableDisable.Enable) listIssue.Add(_pcbaData.ToIssueActualDetail());
                                var consume = TransactionResult.Create(false);
                                if (listIssue.Count > 0)
                                {
                                    lblCommand.Text = @"Container Component Issue.";
                                    consume = await Mes.ExecuteComponentIssue(_mesData, oContainerStatus.ContainerName.Value,
                                        listIssue);
                                }

                                if (consume.Result  || listIssue.Count <=0)
                                {
                                    _dbMoveOut = DateTime.Now;
                                    lblCommand.Text = @"Container Move Standard Attempt 1";
                                    var resultMoveStd = await Mes.ExecuteMoveStandard(_mesData,
                                        oContainerStatus.ContainerName.Value, _dbMoveOut);
                                    if (!resultMoveStd.Result && resultMoveStd.Message.Contains("TimeOut"))
                                    {
                                        _dbMoveOut = DateTime.Now;
                                        lblCommand.Text = @"Container Move Standard Attempt 2";
                                        resultMoveStd = await Mes.ExecuteMoveStandard(_mesData,
                                            oContainerStatus.ContainerName.Value, _dbMoveOut);
                                        if (!resultMoveStd.Result && resultMoveStd.Message.Contains("TimeOut"))
                                        {
                                            _dbMoveOut = DateTime.Now;
                                            lblCommand.Text = @"Container Move Standard Attempt 3";
                                            resultMoveStd = await Mes.ExecuteMoveStandard(_mesData,
                                                oContainerStatus.ContainerName.Value, _dbMoveOut);
                                        }
                                    }

                                   
                                    if (resultMoveStd.Result)
                                    {
                                        lbMoveOut.Text = _dbMoveOut.ToString(Mes.DateTimeStringFormat);
                                        lblCommand.Text = @"Container Component Issue.";
                                        //Update Counter
                                        await Mes.UpdateCounter(_mesData, 1);
                                        var mfg = await Mes.GetMfgOrder(_mesData,
                                            _mesData.ManufacturingOrder.Name.Value);
                                        _mesData.SetManufacturingOrder(mfg);
                                        var count = await Mes.GetCounterFromMfgOrder(_mesData);
                                        Tb_PpaQty.Text = count.ToString();
                                    }
                                    await SetPpaState(resultMoveStd.Result
                                        ? PPAState.ScanUnitSerialNumber
                                        : PPAState.MoveInOkMoveFail);
                                }
                                else
                                {
                                    await SetPpaState(PPAState.ComponentIssueFailed);
                                }
                               
                            }
                            else
                            {// check if fail by maintenance Past Due
                                var transPastDue = Mes.GetMaintenancePastDue(_mesData.MaintenanceStatusDetails);
                                if (transPastDue.Result)
                                {
                                    KryptonMessageBox.Show(this, "This resource under maintenance, need to complete!", "Move In",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                                await SetPpaState(PPAState.MoveInFail);
                            }
                        }
                        else await SetPpaState(PPAState.UnitNotFound);


                    }
                    catch (Exception ex)
                    {
                        ex.Source = typeof(Program).Assembly.GetName().Name == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                        EventLogUtil.LogErrorEvent(ex.Source, ex);
                    }
                    break;
                case PPAState.MoveSuccess:
                    break;
                case PPAState.MoveInOkMoveFail:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Container Move Standard Fail";
                    break;
                case PPAState.MoveInFail:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Container Move In Fail";
                    break;
                case PPAState.WrongOperation:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Incorrect Container Operation";
                    break;
                case PPAState.ComponentNotFound:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Component Not Found";
                    break;
                case PPAState.WrongComponent:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Wrong Component";
                    break;
                case PPAState.WrongProductionOrder:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Mismatch Production Order";
                    break;
                case PPAState.ComponentIssueFailed:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Component Issue Failed.";
                    break;
                case PPAState.WaitPreparation:
                    ClearPo();
                    btnStartPreparation.Enabled = true;
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Wait For Preparation";
                    break;
            }
        }

        private void ClrContainer()
        {
            Tb_Scanner.Clear();
            Tb_SerialNumber.Clear();
            Tb_PumpSerialNumber.Clear();
            Tb_PCBASerialNumber.Clear();
            Tb_PCBAPartNumber.Clear();
            Tb_PumpPartNumber.Clear();
        }

        #endregion

        #region FUNCTION STATUS OF RESOURCE

        private async Task GetStatusMaintenanceDetails()
        {
            try
            {
                var maintenanceStatusDetails = await Mes.GetMaintenanceStatusDetails(_mesData);
                if (maintenanceStatusDetails != null)
                {
                    Dg_Maintenance.DataSource = maintenanceStatusDetails;
                    Dg_Maintenance.Columns["Due"].Visible = false;
                    Dg_Maintenance.Columns["Warning"].Visible = false;
                    Dg_Maintenance.Columns["PastDue"].Visible = false;
                    Dg_Maintenance.Columns["MaintenanceReqName"].Visible = false;
                    Dg_Maintenance.Columns["MaintenanceReqDisplayName"].Visible = false;
                    Dg_Maintenance.Columns["ResourceStatusCodeName"].Visible = false;
                    Dg_Maintenance.Columns["UOMName"].Visible = false;
                    Dg_Maintenance.Columns["ResourceName"].Visible = false;
                    Dg_Maintenance.Columns["UOM2Name"].Visible = false;
                    Dg_Maintenance.Columns["MaintenanceReqRev"].Visible = false;
                    Dg_Maintenance.Columns["NextThruputQty2Warning"].Visible = false;
                    Dg_Maintenance.Columns["NextThruputQty2Limit"].Visible = false;
                    Dg_Maintenance.Columns["UOM2"].Visible = false;
                    Dg_Maintenance.Columns["ThruputQty2"].Visible = false;
                    Dg_Maintenance.Columns["Resource"].Visible = false;
                    Dg_Maintenance.Columns["ResourceStatusCode"].Visible = false;
                    Dg_Maintenance.Columns["NextThruputQty2Due"].Visible = false;
                    Dg_Maintenance.Columns["MaintenanceClassName"].Visible = false;
                    Dg_Maintenance.Columns["MaintenanceStatus"].Visible = false;
                    Dg_Maintenance.Columns["ExportImportKey"].Visible = false;
                    Dg_Maintenance.Columns["DisplayName"].Visible = false;
                    Dg_Maintenance.Columns["Self"].Visible = false;
                    Dg_Maintenance.Columns["IsEmpty"].Visible = false;
                    Dg_Maintenance.Columns["FieldAction"].Visible = false;
                    Dg_Maintenance.Columns["IgnoreTypeDifference"].Visible = false;
                    Dg_Maintenance.Columns["ListItemAction"].Visible = false;
                    Dg_Maintenance.Columns["ListItemIndex"].Visible = false;
                    Dg_Maintenance.Columns["CDOTypeName"].Visible = false;
                    Dg_Maintenance.Columns["key"].Visible = false;
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        private async Task GetStatusOfResource()
        {
            try
            {
                var resourceStatus = await Mes.GetResourceStatusDetails(_mesData);
                _mesData.SetResourceStatusDetails(resourceStatus);

                if (resourceStatus != null)
                {
                    if (resourceStatus.Status != null) Tb_StatusCode.Text = resourceStatus.Reason?.Name;
                    if (resourceStatus.Availability != null)
                    {
                        if (resourceStatus.Availability.Value == "Up")
                        {
                            Tb_StatusCode.StateCommon.Content.Color1 = resourceStatus.Reason?.Name == "Quality Inspection" ? Color.Orange : Color.Green;
                        }
                        else if (resourceStatus.Availability.Value == "Down")
                        {
                            Tb_StatusCode.StateCommon.Content.Color1 = Color.Red;
                        }
                    }
                    else
                    {
                        Tb_StatusCode.StateCommon.Content.Color1 = Color.Orange;
                    }

                    if (resourceStatus.TimeAtStatus != null)
                        Tb_TimeAtStatus.Text = $@"{Mes.OaTimeSpanToString(resourceStatus.TimeAtStatus.Value)}";
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private async Task GetStatusOfResourceDetail()
        {
            try
            {
                var resourceStatus = await Mes.GetResourceStatusDetails(_mesData);
                _mesData.SetResourceStatusDetails(resourceStatus);

                if (resourceStatus != null)
                {
                    if (resourceStatus.Status != null) Cb_StatusCode.Text = resourceStatus.Status.Name;
                    await Task.Delay(1000);
                    if (resourceStatus.Reason != null) Cb_StatusReason.Text = resourceStatus.Reason.Name;
                    if (resourceStatus.Availability != null)
                    {
                        Tb_StatusCodeM.Text = resourceStatus.Availability.Value;
                        if (resourceStatus.Availability.Value == "Up")
                        {
                            Tb_StatusCodeM.StateCommon.Content.Color1 = Color.Green;
                        }
                        else if (resourceStatus.Availability.Value == "Down")
                        {
                            Tb_StatusCodeM.StateCommon.Content.Color1 = Color.Red;
                        }
                    }
                    else
                    {
                        Tb_StatusCodeM.StateCommon.Content.Color1 = Color.Orange;
                    }

                    if (resourceStatus.TimeAtStatus != null)
                        Tb_TimeAtStatus.Text = $@"{Mes.OaTimeSpanToString(resourceStatus.TimeAtStatus.Value)}";
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source
                    ? MethodBase.GetCurrentMethod()?.Name
                    : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private async Task GetResourceStatusCodeList()
        {
            try
            {
                var oStatusCodeList = await Mes.GetListResourceStatusCode(_mesData);
                if (oStatusCodeList != null)
                {
                    Cb_StatusCode.DataSource = oStatusCodeList.Where(x=>x.Name.IndexOf("PPA", StringComparison.Ordinal)==0).ToList();
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        #endregion

        #region COMPONENT EVENT

        private async void TimerRealtime_Tick(object sender, EventArgs e)
        {
            await GetStatusOfResource();
            await GetStatusMaintenanceDetails();
        }
        private async void btnResetState_Click(object sender, EventArgs e)
        {
            if (_ppaState == PPAState.WaitPreparation) return;
            await SetPpaState(PPAState.ScanUnitSerialNumber);
            Tb_Scanner.Focus();
        }

        private bool _readScanner;
        private bool _ignoreScanner;
        private DateTime _dbMoveOut;

        private async void Tb_Scanner_KeyUp(object sender, KeyEventArgs e)
        {
            if (!_readScanner) Tb_Scanner.Clear();
            if (_ignoreScanner) e.Handled = true;
            if (e.KeyCode == Keys.Enter)
            {
                _ignoreScanner = true;
                if (string.IsNullOrEmpty(Tb_Scanner.Text)) return;
                switch (_ppaState)
                {
                    case PPAState.ScanUnitSerialNumber:
                        Tb_SerialNumber.Text = Tb_Scanner.Text.Trim();
                        Tb_Scanner.Clear();

                        await SetPpaState(PPAState.CheckUnitStatus);
                        break;
                    case PPAState.ScanPcbaSerialNumber:
                        if (_pcbaDataConfig.Enable == EnableDisable.Disable)
                        {
                            await SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        }
                        var scannedPcba = Tb_Scanner.Text.Trim();
                        var transactPcba = PcbaData.ParseData(scannedPcba, _pcbaDataConfig);
                        if (transactPcba.Result)
                        {
                            _pcbaData = (PcbaData)transactPcba.Data;
                            Tb_PCBAPartNumber.Text = _pcbaData.PartNumber?.Value;
                            Tb_PCBASerialNumber.Text = scannedPcba;
                            var s = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name == _pcbaData.PartNumber.Value).ToList();
                            if (s.Count == 0)
                            {
                                await SetPpaState(PPAState.ComponentNotFound);
                                break;
                            }

                            if (s[0].wikScanning.Value != "X")
                            {
                                await SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            _pcbaData.SetQtyRequired(s[0].QtyRequired.Value);
                        }
                        else
                        {
                            var l = scannedPcba.Length>10?10:scannedPcba.Length;
                            var str = scannedPcba.Substring(0, l);
                            var vPcba = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name.Contains(str)).ToList();
                            if (vPcba.Count == 0)
                            {
                                await SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            await SetPpaState(PPAState.ComponentNotFound);
                            break;
                        }
                        Tb_Scanner.Clear();
                        await SetPpaState(PPAState.UpdateMoveInMove);
                        break;
                       
                    case PPAState.ScanPumpSerialNumber:
                        if (_pumpDataConfig.Enable == EnableDisable.Disable)
                        {
                            await SetPpaState(PPAState.UpdateMoveInMove);
                            break;
                        }

                        var scannedPump = Tb_Scanner.Text.Trim();
                        var transactPump = PumpData.ParseData(scannedPump, _pumpDataConfig);
                        if (transactPump.Result)
                        {
                            _pumpData = (PumpData)transactPump.Data;
                            Tb_PumpPartNumber.Text = _pumpData.PartNumber?.Value;
                            Tb_PumpSerialNumber.Text = scannedPump;
                            var s = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name == _pumpData.PartNumber.Value).ToList();
                            if (s.Count == 0)
                            {
                                await SetPpaState(PPAState.ComponentNotFound);
                                break;
                            }
                            if (s[0].wikScanning.Value != "X")
                            {
                                await SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            _pumpData.SetQtyRequired(s[0].QtyRequired.Value);
                        }
                        else
                        {
                            var l = scannedPump.Length > 10 ? 10 : scannedPump.Length;
                            var str = scannedPump.Substring(0, l);
                            var vPump = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name.Contains(str)).ToList();
                            if (vPump.Count == 0)
                            {
                                await SetPpaState(PPAState.ComponentNotFound);
                                break;
                            }
                            await SetPpaState(PPAState.WrongComponent);
                            break;
                        }
                        Tb_Scanner.Clear();
                        await SetPpaState(PPAState.UpdateMoveInMove);
                        break;
                    case PPAState.ScanPcbaOrPumpSerialNumber:
                        scannedPcba = Tb_Scanner.Text.Trim();
                        if (scannedPcba.IndexOf("135", StringComparison.Ordinal) == 0)
                        {
                            if (_pcbaDataConfig.Enable == EnableDisable.Disable)
                            {
                                await SetPpaState(PPAState.ScanPumpSerialNumber);
                                break;
                            }

                            transactPcba = PcbaData.ParseData(scannedPcba, _pcbaDataConfig);
                            if (transactPcba.Result)
                            {
                                _pcbaData = (PcbaData)transactPcba.Data;
                                Tb_PCBAPartNumber.Text = _pcbaData.PartNumber?.Value;
                                Tb_PCBASerialNumber.Text = scannedPcba;
                                var s = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name == _pcbaData.PartNumber.Value).ToList();
                                if (s.Count == 0)
                                {
                                    await SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }

                                if (s[0].wikScanning.Value != "X")
                                {
                                    await SetPpaState(PPAState.WrongComponent);
                                    break;
                                }
                                _pcbaData.SetQtyRequired(s[0].QtyRequired.Value);
                            }
                            else
                            {
                                var l = scannedPcba.Length > 10 ? 10 : scannedPcba.Length;
                                var str = scannedPcba.Substring(0, l);
                                var vPcba = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name.Contains(str)).ToList();
                                if (vPcba.Count == 0)
                                {
                                    await SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                await SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            Tb_Scanner.Clear();
                            await SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        }
                        if (scannedPcba.IndexOf("103", StringComparison.Ordinal) == 0)
                        {
                            if (_pumpDataConfig.Enable == EnableDisable.Disable)
                            {
                                await SetPpaState(PPAState.UpdateMoveInMove);
                                break;
                            }
                            scannedPump = scannedPcba;
                            transactPump = PumpData.ParseData(scannedPump, _pumpDataConfig);
                            if (transactPump.Result)
                            {
                                _pumpData = (PumpData)transactPump.Data;
                                Tb_PumpPartNumber.Text = _pumpData.PartNumber?.Value;
                                Tb_PumpSerialNumber.Text = scannedPump;
                                var s = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name == _pumpData.PartNumber.Value && x.wikScanning.Value == "X").ToList();
                                if (s.Count == 0)
                                {
                                    await SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                if (s[0].wikScanning.Value != "X")
                                {
                                    await SetPpaState(PPAState.WrongComponent);
                                    break;
                                }
                                _pumpData.SetQtyRequired(s[0].QtyRequired.Value);
                            }
                            else
                            {
                                var l = scannedPump.Length > 10 ? 10 : scannedPump.Length;
                                var str = scannedPump.Substring(0, l);
                                var vPump = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name.Contains(str)).ToList();
                                if (vPump.Count == 0)
                                {
                                    await SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                await SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            Tb_Scanner.Clear();
                            await SetPpaState(PPAState.ScanPcbaSerialNumber);
                            break;
                        }
                        else
                        {
                            Tb_Scanner.Clear();
                            var l = scannedPcba.Length > 10 ? 10 : scannedPcba.Length;
                            var str = scannedPcba.Substring(0, l);
                            var vPcba = _mesData.ManufacturingOrder.MaterialList.Where(x => x.Product.Name.Contains(str)).ToList();
                            if (vPcba.Count == 0)
                            {
                                await SetPpaState(PPAState.ComponentNotFound);
                                break;
                            }
                            await SetPpaState(PPAState.WrongComponent);
                            break;
                        }

                }
                _ignoreScanner = false;
                Tb_Scanner.Clear();
            }
        }
        #endregion

        private async void Main_Load(object sender, EventArgs e)
        {
            await GetStatusOfResource();
            await GetStatusMaintenanceDetails();
            await GetResourceStatusCodeList();
            await SetPpaState(PPAState.WaitPreparation);
        }

        private void ClearPo()
        {
            Tb_PO.Clear();
            Tb_Product.Clear();
            Tb_ProductDesc.Clear();
            Tb_PpaQty.Clear();
            Tb_FinishedGoodCounter.Clear();
            pictureBox1.ImageLocation = null;
            ClrContainer();
        }

        private void kryptonGroupBox2_Panel_Paint(object sender, PaintEventArgs e)
        {

        }

        private async void Cb_StatusCode_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                var oStatusCode = await Mes.GetResourceStatusCode(_mesData, Cb_StatusCode.SelectedValue != null ? Cb_StatusCode.SelectedValue.ToString() : "");
                if (oStatusCode != null)
                {
                    Tb_StatusCodeM.Text = oStatusCode.Availability.ToString();
                    if (oStatusCode.ResourceStatusReasons != null)
                    {
                        var oStatusReason = await Mes.GetResourceStatusReasonGroup(_mesData, oStatusCode.ResourceStatusReasons.Name);
                        Cb_StatusReason.DataSource = oStatusReason.Entries;
                    }
                    else
                    {
                        Cb_StatusReason.Items.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        private async void btnSetMachineStatus_Click(object sender, EventArgs e)
        {
            try
            {
                var result = false;
                if (Cb_StatusCode.Text != "" && Cb_StatusReason.Text != "")
                {
                    result = await Mes.SetResourceStatus(_mesData, Cb_StatusCode.Text, Cb_StatusReason.Text);
                }
                else if (Cb_StatusCode.Text != "")
                {
                    result = await Mes.SetResourceStatus(_mesData, Cb_StatusCode.Text, "");
                }

                await GetStatusOfResourceDetail();
                await GetStatusOfResource();
                KryptonMessageBox.Show(result ? "Setup status successful" : "Setup status failed");

            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private async void kryptonNavigator1_SelectedPageChanged(object sender, EventArgs e)
        {
            if (kryptonNavigator1.SelectedIndex == 1)
            {
                await GetStatusOfResourceDetail();
            }
            if (kryptonNavigator1.SelectedIndex == 2)
            {
                _tempPcba = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
                Ppg_Pcba.SelectedObject = _tempPcba;
                _tempPump = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
                Ppg_Pump.SelectedObject = _tempPump;
            }
            if (kryptonNavigator1.SelectedIndex == 3)
            {
                lblPo.Text = $@"Serial Number of PO: {_mesData.ManufacturingOrder?.Name}";
                lblLoading.Visible = true;
                await GetFinishedGoodRecord();
                lblLoading.Visible = false;
            }

        }
        private async Task GetFinishedGoodRecord()
        {
            var data = await Mes.GetFinishGoodRecord(_mesData, _mesData.ManufacturingOrder?.Name.ToString());
            if (data != null)
            {
                var list = await Mes.ContainerStatusesToFinishedGood(data);
                finishedGoodBindingSource.DataSource = new BindingList<FinishedGood>(list);
                kryptonDataGridView1.DataSource = finishedGoodBindingSource;
                Tb_FinishedGoodCounter.Text = list.Length.ToString();
            }
        }
        private void Btn_SetPcba_Click(object sender, EventArgs e)
        {
            _tempPcba.SaveToFile();
            _pcbaDataConfig = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
        }

        private void Btn_SetPump_Click(object sender, EventArgs e)
        {
            _tempPump.SaveToFile();
            _pumpDataConfig = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);

        }
        private void kryptonNavigator1_Selecting(object sender, ComponentFactory.Krypton.Navigator.KryptonPageCancelEventArgs e)
        {
            if (e.Index != 1 && e.Index != 2) return;

            using (var ss = new LoginForm24())
            {
                var dlg = ss.ShowDialog(this);
                if (dlg == DialogResult.Abort)
                {
                    KryptonMessageBox.Show("Login Failed");
                    e.Cancel = true;
                    return;
                }
                if (dlg == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (ss.UserDetails.UserRole == UserRole.Maintenance && e.Index != 1) e.Cancel = true;
                if (ss.UserDetails.UserRole == UserRole.Quality && e.Index != 2) e.Cancel = true;
            }


        }

        private async void btnCallMaintenance_Click(object sender, EventArgs e)
        {
            try
            {
                var result = await Mes.SetResourceStatus(_mesData, "PPA - Internal Downtime", "Maintenance");
                await GetStatusOfResource();
                KryptonMessageBox.Show(result ? "Setup status successful" : "Setup status failed");

            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private async void btnFinishPreparation_Click(object sender, EventArgs e)
        {
            if (_mesData.ResourceStatusDetails == null) return;
            if (_mesData.ResourceStatusDetails?.Reason?.Name == "Maintenance") return;
            var result = await Mes.SetResourceStatus(_mesData, "PPA - Productive Time", "Pass");
            await GetStatusOfResource();
            if (result)
            {
                btnFinishPreparation.Enabled = false;
                btnStartPreparation.Enabled = true;
                await SetPpaState(PPAState.ScanUnitSerialNumber);
            }
        }

        private async void btnStartPreparation_Click(object sender, EventArgs e)
        {
            ClearPo();
            if (_mesData.ResourceStatusDetails == null) return;
            if (_mesData.ResourceStatusDetails?.Reason?.Name == "Maintenance") return;
            _mesData.SetManufacturingOrder(null);
            var result = await Mes.SetResourceStatus(_mesData, "PPA - Planned Downtime", "Preparation");
            await GetStatusOfResource();
            if (result)
            {
                btnFinishPreparation.Enabled = true;
                btnStartPreparation.Enabled = false;
            }
        }

        private void kryptonPanel9_Paint(object sender, PaintEventArgs e)
        {

        }

        private void Tb_Scanner_TextChanged(object sender, EventArgs e)
        {

        }

        private void Dg_Maintenance_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in Dg_Maintenance.Rows)
                {
                    //Console.WriteLine(Convert.ToString(row.Cells["MaintenanceState"].Value));
                    if (Convert.ToString(row.Cells["MaintenanceState"].Value) == "Pending")
                    {
                        row.DefaultCellStyle.BackColor = Color.Yellow;
                    }
                    else if (Convert.ToString(row.Cells["MaintenanceState"].Value) == "Due")
                    {
                        row.DefaultCellStyle.BackColor = Color.Orange;
                    }
                    else if (Convert.ToString(row.Cells["MaintenanceState"].Value) == "Past Due")
                    {
                        row.DefaultCellStyle.BackColor = Color.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private void Dg_Maintenance_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }
    }
}
