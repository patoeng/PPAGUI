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
            Rectangle r = new Rectangle(0, 0, Pb_IndicatorPicture.Width, Pb_IndicatorPicture.Height);
            System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath();
            int d = 28;
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
            Size = new Size(820, 810);
            MyTitle.Text = $@"PCBA and Pump - {AppSettings.Resource}";
            ResourceGrouping.Values.Heading = $@"Resource Status: {AppSettings.Resource}";
            ResourceDataGroup.Values.Heading = $@"Resource Data Collection: {AppSettings.Resource}";
            Text = Mes.AddVersionNumber(Text);
          
        }

#endregion

#region INSTANCE VARIABLE
      
        private PPAState _ppaState;
        private readonly Mes _mesData;
        private DateTime _dMoveIn;

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
                    Tb_SerialNumber.Clear();
                    Tb_PCBASerialNumber.Clear();
                    Tb_PumpSerialNumber.Clear();
                    Tb_Operation.Clear();
                    Tb_ContainerPosition.Clear();
                    Tb_PO.Clear();

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
                    lblCommand.Text = "Container Move In";
                    /*Move In, Move*/
                    try
                    {
                        var cDataPoint = new DataPointDetails[2];
                        cDataPoint[0] = new DataPointDetails { DataName = "PCBA Serial Number", DataValue = Tb_PCBASerialNumber.Text != "" ? Tb_PCBASerialNumber.Text : "NA", DataType = DataTypeEnum.String };
                        cDataPoint[1] = new DataPointDetails { DataName = "Pump Serial Number", DataValue = Tb_PumpSerialNumber.Text != "" ? Tb_PumpSerialNumber.Text : "NA", DataType = DataTypeEnum.String };
                        oContainerStatus = await Mes.GetContainerStatusDetails(_mesData,Tb_SerialNumber.Text);
                        if (oContainerStatus.ContainerName != null)
                        {
                            var resultMoveIn = await Mes.ExecuteMoveIn(_mesData, oContainerStatus.ContainerName.Value,_dMoveIn);
                            if (resultMoveIn)
                            {
                                lblCommand.Text = "Container Move Standard";
                                var resultMoveStd = await Mes.ExecuteMoveStandard(_mesData, oContainerStatus.ContainerName.Value, DateTime.Now, cDataPoint);
                                await SetPpaState(resultMoveStd
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
                    lblCommand.Text = "Container Move Standard Fail";
                    break;
                case PPAState.MoveInFail:
                    lblCommand.ForeColor = Color.Red;
                    Tb_Scanner.Enabled = false;
                    lblCommand.Text = "Container Move In Fail";
                    break;
                case PPAState.WrongOperation:
                    lblCommand.ForeColor = Color.Red;
                    Tb_Scanner.Enabled = false;
                    lblCommand.Text = @"Incorrect Container Operation";
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
                            Tb_PCBASerialNumber.Text = Tb_Scanner.Text.Trim();
                            Tb_Scanner.Clear();
                            await SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        case PPAState.ScanPumpSerialNumber:
                            Tb_PumpSerialNumber.Text = Tb_Scanner.Text.Trim();
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
    }
}
