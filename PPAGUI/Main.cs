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
using OpcenterWikLibrary;

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
            GetResourceStatusCodeList();
            GetStatusOfResource();
            GetStatusMaintenanceDetails();
            Cb_StatusCode.SelectedItem = null;
            Cb_StatusReason.SelectedItem = null;
            Tb_SetupAvailability.Text = "";

            this.WindowState = FormWindowState.Normal;
            this.Size = new Size(810, 800);
            MyTitle.Text = $"PCBA and Pump - {AppSettings.Resource}";
            ResourceGrouping.Values.Heading = $"Resource Status: {AppSettings.Resource}";
            ResourceSetupGrouping.Values.Heading = $"Resource Setup: {AppSettings.Resource}";
            ResourceDataGroup.Values.Heading = $"Resource Data Collection: {AppSettings.Resource}";
            AddVersionNumber();
        }
        #endregion

        #region INSTANCE VARIABLE
        private static GetMaintenanceStatusDetails[] oMaintenanceStatus = null;
        private static ServiceUtil oServiceUtil = new ServiceUtil();
        #endregion

        #region FUNCTION USEFULL
        private void AddVersionNumber()
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);

            this.Text += $" V.{versionInfo.FileVersion}";
        }
        #endregion

        #region FUNCTION STATUS OF RESOURCE
        private void GetResourceStatusCodeList()
        {
            NamedObjectRef[] oStatusCodeList = oServiceUtil.GetListResourceStatusCode();
            if (oStatusCodeList != null)
            {
                Cb_StatusCode.DataSource = oStatusCodeList;
            }
        }
        private void GetStatusMaintenanceDetails()
        {
            try
            {
                oMaintenanceStatus = oServiceUtil.GetGetMaintenanceStatus(AppSettings.Resource);
                if (oMaintenanceStatus != null)
                {
                    Dg_Maintenance.DataSource = oMaintenanceStatus;
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
                ex.Source = typeof(Program).Assembly.GetName().Name == ex.Source ? MethodBase.GetCurrentMethod().Name : MethodBase.GetCurrentMethod().Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        private void GetStatusOfResource()
        {
            try
            {
                ResourceStatusDetails oResourceStatusDetails = oServiceUtil.GetResourceStatusDetails(AppSettings.Resource);
                if (oResourceStatusDetails != null)
                {
                    if (oResourceStatusDetails.Status != null) Tb_StatusCode.Text = oResourceStatusDetails.Status.Name;
                    if (oResourceStatusDetails.Reason != null) Tb_StatusReason.Text = oResourceStatusDetails.Reason.Name;
                    if (oResourceStatusDetails.Availability != null)
                    {
                        Tb_Availability.Text = oResourceStatusDetails.Availability.Value;
                        if (oResourceStatusDetails.Availability.Value == "Up")
                        {
                            Pb_IndicatorPicture.BackColor = Color.Green;
                        }
                        else if (oResourceStatusDetails.Availability.Value == "Down")
                        {
                            Pb_IndicatorPicture.BackColor = Color.Red;
                        }
                    }
                    else
                    {
                        Pb_IndicatorPicture.BackColor = Color.Orange;
                    }
                    if (oResourceStatusDetails.TimeAtStatus != null) Tb_TimeAtStatus.Text = Convert.ToString(oResourceStatusDetails.TimeAtStatus.Value);
                }
            }
            catch (Exception ex)
            {
                ex.Source = typeof(Program).Assembly.GetName().Name == ex.Source ? MethodBase.GetCurrentMethod().Name : MethodBase.GetCurrentMethod().Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        #endregion

        #region COMPONENT EVENT
        private void Bt_FindContainer_Click(object sender, EventArgs e)
        {
            Tb_Operation.Clear();
            Tb_PO.Clear();
            Tb_ContainerPosition.Clear();
            CurrentContainerStatus oContainerStatus = oServiceUtil.GetContainerStatusDetails(Tb_SerialNumber.Text);
            Tb_ContainerPosition.Text = oServiceUtil.GetCurrentContainerStep(Tb_SerialNumber.Text);
            if (oContainerStatus != null)
            {
                if (oContainerStatus.MfgOrderName != null) Tb_PO.Text = oContainerStatus.MfgOrderName.ToString();
                if (oContainerStatus.Operation != null) Tb_Operation.Text = oContainerStatus.Operation.Name.ToString();
            }
        }
        private void Bt_StartMove_Click(object sender, EventArgs e)
        {
            try
            {
                bool resultMoveIn = false;
                bool resultMoveStd = false;
                Camstar.WCF.ObjectStack.DataPointDetails[] cDataPoint = new Camstar.WCF.ObjectStack.DataPointDetails[2];
                cDataPoint[0] = new Camstar.WCF.ObjectStack.DataPointDetails() { DataName = "PCBA Serial Number", DataValue = Tb_PCBASerialNumber.Text != "" ? Tb_PCBASerialNumber.Text : "NA", DataType = DataTypeEnum.String };
                cDataPoint[1] = new Camstar.WCF.ObjectStack.DataPointDetails() { DataName = "Pump Serial Number", DataValue = Tb_PumpSerialNumber.Text != "" ? Tb_PumpSerialNumber.Text : "NA", DataType = DataTypeEnum.String };
                CurrentContainerStatus oContainerStatus = oServiceUtil.GetContainerStatusDetails(Tb_SerialNumber.Text);
                if (oContainerStatus.ContainerName != null)
                {
                    resultMoveIn = oServiceUtil.ExecuteMoveIn(oContainerStatus.ContainerName.Value, AppSettings.Resource);
                    if (resultMoveIn)
                    {
                        resultMoveStd = oServiceUtil.ExecuteMoveStd(oContainerStatus.ContainerName.Value, "", AppSettings.Resource, "Group of Manual Assy Data", "", cDataPoint);
                        if (resultMoveStd) MessageBox.Show("MoveIn and MoveStd success!");
                        else MessageBox.Show("Move In success and but Move Std Fail!");
                    }
                    else MessageBox.Show("Move In and Move Std Fail!");
                }
                else MessageBox.Show($"Container {Tb_SerialNumber.Text} not found!");
            }
            catch (Exception ex)
            {
                ex.Source = typeof(Program).Assembly.GetName().Name == ex.Source ? MethodBase.GetCurrentMethod().Name : MethodBase.GetCurrentMethod().Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        private void Cb_StatusCode_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                ResourceStatusCodeChanges oStatusCode = oServiceUtil.GetResourceStatusCode(Cb_StatusCode.SelectedValue != null ? Cb_StatusCode.SelectedValue.ToString() : "");
                if (oStatusCode != null)
                {
                    Tb_SetupAvailability.Text = oStatusCode.Availability.ToString();
                    if (oStatusCode.ResourceStatusReasons != null)
                    {
                        ResStatusReasonGroupChanges oStatusReason = oServiceUtil.GetResourceStatusReasonGroup(oStatusCode.ResourceStatusReasons.Name);
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
                ex.Source = typeof(Program).Assembly.GetName().Name == ex.Source ? MethodBase.GetCurrentMethod().Name : MethodBase.GetCurrentMethod().Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        private void Bt_SetResourceStatus_Click(object sender, EventArgs e)
        {
            try
            {
                if (Cb_StatusCode.Text != "" && Cb_StatusReason.Text != "")
                {
                    oServiceUtil.ExecuteResourceSetup(AppSettings.Resource, Cb_StatusCode.Text, Cb_StatusReason.Text);
                }
                else if (Cb_StatusCode.Text != "")
                {
                    oServiceUtil.ExecuteResourceSetup(AppSettings.Resource, Cb_StatusCode.Text, "");
                }
                GetStatusOfResource();
                GetStatusMaintenanceDetails();
            }
            catch (Exception ex)
            {
                ex.Source = typeof(Program).Assembly.GetName().Name == ex.Source ? MethodBase.GetCurrentMethod().Name : MethodBase.GetCurrentMethod().Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
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
                ex.Source = typeof(Program).Assembly.GetName().Name == ex.Source ? MethodBase.GetCurrentMethod().Name : MethodBase.GetCurrentMethod().Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }
        private void TimerRealtime_Tick(object sender, EventArgs e)
        {
            GetStatusOfResource();
            GetStatusMaintenanceDetails();
        }
        private void Cb_StatusCode_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = true;
        }
        private void Cb_StatusReason_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = true;
        }
        #endregion
    }
}
