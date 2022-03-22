using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Camstar.WCF.ObjectStack;
using ComponentFactory.Krypton.Toolkit;
using MesData;
using MesData.Ppa;
using OpcenterWikLibrary;
using PPAGUI.Enumeration;

namespace PPAGUI
{
    public partial class Main : KryptonForm
    {
        #region CONSTRUCTOR
        public Main()
        {
            InitializeComponent();
            var r = new Rectangle(0, 0, Pb_IndicatorPicture.Width, Pb_IndicatorPicture.Height);
            var gp = new System.Drawing.Drawing2D.GraphicsPath();
            var d = 28;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.X + r.Width - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.X + r.Width - d, r.Y + r.Height - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Y + r.Height - d, d, d, 90, 90);
            Pb_IndicatorPicture.Region = new Region(gp);


#if MiniMe
            var  name = "Pump & PCBA Assy Minime";
            Text = Mes.AddVersionNumber(Text + " MiniMe");
#elif Ariel
            var  name = "Pump & PCBA Assy Ariel";
            Text = Mes.AddVersionNumber(Text + " Ariel");
#endif
            _mesData = new Mes(name);

            WindowState = FormWindowState.Normal;
            Size = new Size(1134, 701);
            MyTitle.Text = $@"PCBA and Pump - {AppSettings.Resource}";
            ResourceGrouping.Values.Heading = $@"Resource Status: {AppSettings.Resource}";
            ResourceDataGroup.Values.Heading = $@"Resource Data Collection: {AppSettings.Resource}";
            //Text = Mes.AddVersionNumber(Text);

            _pcbaDataConfig = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
            _pcbaDataConfig?.SaveToFile();

            _pumpDataConfig = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
            _pumpDataConfig?.SaveToFile();
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
        private  PcbaDataPointConfig _pcbaDataConfig;
        private  PumpDataPointConfig _pumpDataConfig;

        #endregion

#region FUNCTION USEFULL
        
        private async Task SetPpaState(PPAState newPpaState)
        {
            _ppaState = newPpaState;
            switch (_ppaState)
            {
                case PPAState.PlaceUnit:
                    Tb_Scanner.Enabled = false;
                    lblCommand.ForeColor = Color.Red;
                    lblCommand.Text = "Resource is not in \"Up\" condition!";
                    break;
                case PPAState.ScanUnitSerialNumber:
                    Tb_Scanner.Clear();
                    Tb_SerialNumber.Clear();
                    Tb_PCBAPartNumber.Clear();
                    Tb_PCBAVendor.Clear();
                    Tb_PCBAVoltage.Clear();
                    Tb_PCBAHardware.Clear();
                    Tb_PCBASoftware.Clear();
                    Tb_PCBAMfgDate.Clear();
                    Tb_PCBAUnique.Clear();

                    Tb_PumpPartNumber.Clear();
                    Tb_PumpVendor.Clear();
                    Tb_PumpVoltage.Clear();
                    Tb_PumpLotNumber.Clear();
                    Tb_PumpMfgDate.Clear();
                    Tb_PumpUnique.Clear();

                    Tb_Operation.Clear();
                    Tb_ContainerPosition.Clear();
                    Tb_PO.Clear();

                    _pcbaData = new PcbaData();
                    _pumpData = new PumpData();

                    if (_mesData.ResourceStatusDetails == null || _mesData.ResourceStatusDetails?.Availability != "Up")
                    {
                        await SetPpaState(PPAState.PlaceUnit);
                        break;
                    }

                    Tb_Scanner.Enabled = true;
                    lblCommand.ForeColor = Color.LimeGreen;
                    lblCommand.Text = "Scan Unit Serial Number!";
                    ActiveControl = Tb_Scanner;
                    break;
                case PPAState.CheckUnitStatus:
                    Tb_Scanner.Enabled = false;
                    lblCommand.Text = "Checking Unit Status";
                    var oContainerStatus = await Mes.GetContainerStatusDetails(_mesData,Tb_SerialNumber.Text,_mesData.DataCollectionName);
                    Tb_ContainerPosition.Text = await Mes.GetCurrentContainerStep(_mesData, Tb_SerialNumber.Text);
                    if (oContainerStatus != null)
                    {
                        if (oContainerStatus.MfgOrderName != null) Tb_PO.Text = oContainerStatus.MfgOrderName.ToString();
                        if (oContainerStatus.Operation != null)
                        {
                            Tb_Operation.Text = oContainerStatus.Operation.Name;
                            if (oContainerStatus.Operation.Name != _mesData.DataCollectionName)
                            {
                                await SetPpaState(PPAState.WrongOperation);
                                break;
                            }
                        }
                        _dMoveIn = DateTime.Now;
                        await SetPpaState(PPAState.ScanPcbaSerialNumber);
                        break;
                    }
                    await SetPpaState(PPAState.UnitNotFound);
                    break;
                case PPAState.UnitNotFound:
                    Tb_Scanner.Enabled = false;
                    lblCommand.ForeColor = Color.Red;
                    lblCommand.Text = "Unit Not Found";
                    break;
                case PPAState.ScanPcbaSerialNumber:
                    Tb_Scanner.Enabled = true;
                    lblCommand.Text = "Scan PCBA Serial Number!";
                    break;
                case PPAState.ScanPumpSerialNumber:
                    Tb_Scanner.Enabled = true;
                    lblCommand.Text = "Scan Pump Serial Number!";
                    break;
                case PPAState.Done:
                    break;
                case PPAState.UpdateMoveInMove:
                    Tb_Scanner.Enabled = false;
                   
                    /*Move In, Move*/
                    try
                    {
                        var cDataPoint = _pumpData.ToDataPointDetailsList();
                        cDataPoint.AddRange(_pcbaData.ToDataPointDetailsList());

                        oContainerStatus = await Mes.GetContainerStatusDetails(_mesData,Tb_SerialNumber.Text);
                        if (oContainerStatus.ContainerName != null)
                        {
                            lblCommand.Text = @"Container Move In Attempt 1";
                            var transaction = await Mes.ExecuteMoveIn(_mesData, oContainerStatus.ContainerName.Value,_dMoveIn);
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
                                lblCommand.Text = @"Container Move Standard Attempt 1";
                                var resultMoveStd = await Mes.ExecuteMoveStandard(_mesData, oContainerStatus.ContainerName.Value, DateTime.Now, cDataPoint.ToArray());
                                if (!resultMoveStd.Result && resultMoveStd.Message.Contains("TimeOut"))
                                {
                                    lblCommand.Text = @"Container Move Standard Attempt 2";
                                    resultMoveStd = await Mes.ExecuteMoveStandard(_mesData, oContainerStatus.ContainerName.Value, DateTime.Now, cDataPoint.ToArray());
                                    if (!resultMoveStd.Result && resultMoveStd.Message.Contains("TimeOut"))
                                    {
                                        lblCommand.Text = @"Container Move Standard Attempt 3";
                                        resultMoveStd = await Mes.ExecuteMoveStandard(_mesData, oContainerStatus.ContainerName.Value, DateTime.Now, cDataPoint.ToArray());
                                    }
                                }
                                await SetPpaState(resultMoveStd.Result
                                        ? PPAState.ScanUnitSerialNumber
                                        : PPAState.MoveInOkMoveFail);
                            }
                            else await SetPpaState(PPAState.MoveInFail);
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
                    Tb_Scanner.Enabled = false;
                    lblCommand.Text = @"Container Move Standard Fail";
                    break;
                case PPAState.MoveInFail:
                    lblCommand.ForeColor = Color.Red;
                    Tb_Scanner.Enabled = false;
                    lblCommand.Text = @"Container Move In Fail";
                    break;
                case PPAState.WrongOperation:
                    lblCommand.ForeColor = Color.Red;
                    Tb_Scanner.Enabled = false;
                    lblCommand.Text = @"Incorrect Container Operation";
                    break;
                case PPAState.IncorrectDataFormat:
                    lblCommand.ForeColor = Color.Red;
                    Tb_Scanner.Enabled = false;
                    lblCommand.Text = @"Incorrect Data Format";
                    break;
            }
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
                    if (resourceStatus.Status != null) Tb_StatusCode.Text = resourceStatus.Status.Name;
                    if (resourceStatus.Reason != null) Tb_StatusReason.Text = resourceStatus.Reason.Name;
                    if (resourceStatus.Availability != null)
                    {
                        Tb_Availability.Text = resourceStatus.Availability.Value;
                        if (resourceStatus.Availability.Value == "Up")
                        {
                            Pb_IndicatorPicture.BackColor = Color.Green;
                        }
                        else if (resourceStatus.Availability.Value == "Down")
                        {
                            Pb_IndicatorPicture.BackColor = Color.Red;
                        }
                    }
                    else
                    {
                        Pb_IndicatorPicture.BackColor = Color.Orange;
                    }

                    if (resourceStatus.TimeAtStatus != null)
                        Tb_TimeAtStatus.Text =
                            $@"{DateTime.FromOADate(resourceStatus.TimeAtStatus.Value) - Mes.ZeroEpoch():G}";

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
            await SetPpaState(PPAState.ScanUnitSerialNumber);
            Tb_Scanner.Focus();
        }

        private async void Tb_Scanner_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (string.IsNullOrEmpty(Tb_Scanner.Text))return;
                    switch (_ppaState)
                    {
                        case PPAState.ScanUnitSerialNumber:
                            Tb_SerialNumber.Text = Tb_Scanner.Text.Trim();
                            Tb_Scanner.Clear();
                            Tb_Operation.Clear();
                            Tb_PO.Clear();
                            Tb_ContainerPosition.Clear();
                            await SetPpaState(PPAState.CheckUnitStatus);
                            break;
                        case PPAState.ScanPcbaSerialNumber:
                            var scannedPcba = Tb_Scanner.Text.Trim();
                            var transactPcba = PcbaData.ParseData(scannedPcba, _pcbaDataConfig);
                            if (transactPcba.Result)
                            {
                                _pcbaData = (PcbaData) transactPcba.Data;
                                Tb_PCBAPartNumber.Text = _pcbaData.PartNumber?.Value;
                                Tb_PCBAVendor.Text = _pcbaData.Vendor?.Value;
                                Tb_PCBAVoltage.Text = _pcbaData.Voltage?.Value;
                                Tb_PCBAHardware.Text = _pcbaData.Hardware?.Value;
                                Tb_PCBASoftware.Text = _pcbaData.Software?.Value;
                                Tb_PCBAMfgDate.Text = _pcbaData.MfgDate?.Value;
                                Tb_PCBAUnique.Text = _pcbaData.Unique?.Value;
                            }
                            else
                            {
                                await SetPpaState(PPAState.IncorrectDataFormat);
                                break;
                            }
                            Tb_Scanner.Clear();
                            await SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        case PPAState.ScanPumpSerialNumber:
                            var scannedPump = Tb_Scanner.Text.Trim();
                            var transactPump = PumpData.ParseData(scannedPump, _pumpDataConfig);
                            if (transactPump.Result)
                            {
                                _pumpData = (PumpData) transactPump.Data;
                                Tb_PumpPartNumber.Text = _pumpData.PartNumber?.Value ;
                                Tb_PumpVendor.Text = _pumpData.Vendor?.Value ;
                                Tb_PumpVoltage.Text = _pumpData.Voltage?.Value ;
                                Tb_PumpLotNumber.Text = _pumpData.LotNumber?.Value ;
                                Tb_PumpMfgDate.Text = _pumpData.MfgDate?.Value ;
                                Tb_PumpUnique.Text = _pumpData.Unique?.Value ;
                            }else
                            {
                                await SetPpaState(PPAState.IncorrectDataFormat);
                                break;
                            }
                        Tb_Scanner.Clear();
                            await SetPpaState(PPAState.UpdateMoveInMove);
                            break;
                    }
            }
        }
#endregion

        private async void Main_Load(object sender, EventArgs e)
        {
            await GetStatusOfResource();
            await GetStatusMaintenanceDetails();
            await SetPpaState(PPAState.ScanUnitSerialNumber);
        }

        private async void btnResourceSetup_Click(object sender, EventArgs e)
        {
            Mes.ResourceSetupForm(this,_mesData, MyTitle.Text);
            await GetStatusOfResource();
        }

        private void Btn_PcbaSetup_Click(object sender, EventArgs e)
        {
            var dialog = _pcbaDataConfig.ShowForm(PcbaDataPointConfig.FileName);
            if (dialog != DialogResult.Yes) return;

            _pcbaDataConfig = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
        }

        private void Btn_PumpSetup_Click(object sender, EventArgs e)
        {
            var dialog = _pumpDataConfig.ShowForm(PumpDataPointConfig.FileName);
            if (dialog != DialogResult.Yes) return;

            _pumpDataConfig = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
        }
    }
}
