using ComponentFactory.Krypton.Toolkit;
using MesData;
using MesData.Login;
using MesData.Ppa;
using OpcenterWikLibrary;
using PPAGUI.Enumeration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Camstar.WCF.ObjectStack;
using MesData.Repair;
using PPAGUI.Hardware;
using PPAGUI.Properties;

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
            _mesData = new Mes("", AppSettings.Resource, name);

            WindowState = FormWindowState.Normal;
            Size = new Size(1134, 701);
            lbTitle.Text = AppSettings.Resource;

            _pcbaDataConfig = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
            _pcbaDataConfig?.SaveToFile();

            _pumpDataConfig = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
            _pumpDataConfig?.SaveToFile();

            kryptonNavigator1.SelectedIndex = 0;
            EventLogUtil.LogEvent("Application Start");

            //Prepare Maintenance Grid
            var maintStrings = new[] { "Resource", "MaintenanceType", "MaintenanceReq", "NextDateDue", "NextThruputQtyDue", "MaintenanceState" };
           
            for (int i = 0; i < Dg_Maintenance.Columns.Count; i++)
            {
                if (!maintStrings.Contains(Dg_Maintenance.Columns[i].DataPropertyName))
                {
                    Dg_Maintenance.Columns[i].Visible = false;
                }
                else
                {
                    switch (Dg_Maintenance.Columns[i].HeaderText)
                    {

                        case "MaintenanceType":
                            Dg_Maintenance.Columns[i].HeaderText = @"Maintenance Type";
                            break;
                        case "MaintenanceReq":
                            Dg_Maintenance.Columns[i].HeaderText = @"Maintenance Requirement";
                            break;
                        case "NextDateDue":
                            Dg_Maintenance.Columns[i].HeaderText = @"Next Due Date";
                            break;
                        case "NextThruputQtyDue":
                            Dg_Maintenance.Columns[i].HeaderText = @"Next Thruput Quantity Due";
                            break;
                        case "MaintenanceState":
                            Dg_Maintenance.Columns[i].HeaderText = @"Maintenance State";
                            _indexMaintenanceState = Dg_Maintenance.Columns[i].Index;
                            break;
                    }

                }
            }
            //Instantiate Setting
            var setting = new Settings();
            //Init Com
            var serialCom = new SerialPort
            {
                PortName = setting.PortName,
                BaudRate = setting.BaudRate,
                Parity = setting.Parity,
                DataBits = setting.DataBits,
                StopBits = setting.StopBits
            };

            _keyenceRs232Scanner = new Rs232Scanner(serialCom);
            _keyenceRs232Scanner.OnDataReadValid += KeyenceDataReadValid;
        }
        private async Task KeyenceDataReadValid(object sender)
        {
            if (!_readScanner) Tb_Scanner.Clear();
            _ignoreScanner = true;
            if (string.IsNullOrEmpty(_keyenceRs232Scanner.DataValue) ) return;
            switch (_ppaState)
            {
                case PPAState.ScanUnitSerialNumber:
                    Tb_SerialNumber.Text = _keyenceRs232Scanner.DataValue.Trim('\r','\n');
                    Tb_Scanner.Clear();
                    await SetPpaState(PPAState.CheckUnitStatus);
                    break;
            }
            _ignoreScanner = false;
            Tb_Scanner.Clear();
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
        private string _wrongOperationPosition;

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
                    lblCommand.Text = @"Resource is not in ""Up"" condition!";
                    break;
                case PPAState.ScanUnitSerialNumber:
                    lblCommand.ForeColor = Color.LimeGreen;
                    lblCommand.Text = @"Scan Unit Serial Number!";
                    ClrContainer();
                    _keyenceRs232Scanner.StopRead();

                    _pcbaData = new PcbaData();
                    _pumpData = new PumpData();

                    if (_mesData.ResourceStatusDetails == null || _mesData.ResourceStatusDetails?.Availability != "Up")
                    {
                        await SetPpaState(PPAState.PlaceUnit);
                        break;
                    }
                    // check if fail by maintenance Past Due
                    var transPastDue = Mes.GetMaintenancePastDue(_mesData.MaintenanceStatusDetails);
                    if (transPastDue.Result)
                    {
                        KryptonMessageBox.Show(this, "This resource under maintenance, need to complete!", "Move In",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        break;
                    }
                    Tb_Scanner.Enabled = true;
                    _readScanner = true;
                    ActiveControl = Tb_Scanner;
                    _keyenceRs232Scanner.StartRead();
                    break;
                case PPAState.CheckUnitStatus:
                    _afterRepair = false;
                    _readScanner = false;
                    _oldPcba = "";
                    _oldPump = "";
                    _keyenceRs232Scanner.StopRead();
                    lblCommand.Text = @"Checking Unit Status";
                    if (_mesData.ResourceStatusDetails == null || _mesData.ResourceStatusDetails?.Availability != "Up")
                    {
                        await SetPpaState(PPAState.PlaceUnit);
                        break;
                    }
                    // check if fail by maintenance Past Due
                     transPastDue = Mes.GetMaintenancePastDue(_mesData.MaintenanceStatusDetails);
                    if (transPastDue.Result)
                    {
                        KryptonMessageBox.Show(this, "This resource under maintenance, need to complete!", "Move In",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        break;
                    }
                    var oContainerStatus = await Mes.GetContainerStatusDetails(_mesData, Tb_SerialNumber.Text, _mesData.DataCollectionName);
                    if (oContainerStatus != null)
                    {

                        if (oContainerStatus.Operation != null)
                        {

                            if (oContainerStatus.Operation.Name != _mesData.OperationName)
                            {
                                _wrongOperationPosition = oContainerStatus.Operation.Name;
                                   await SetPpaState(PPAState.WrongOperation);
                                break;
                            }
                        }
                        _dMoveIn = DateTime.Now;
                        lbMoveIn.Text = _dMoveIn.ToString(Mes.DateTimeStringFormat);
                        lbMoveOut.Text = "";
                        //get AfterRepair Atribute
                        var afterRepair = oContainerStatus.Attributes.Where(x => x.Name == "AfterRepair").ToList();
                        _afterRepair = afterRepair.Count > 0 && afterRepair[0].AttributeValue == "Yes";
                        lbAfterRepair.Text = _afterRepair ? "Yes" : "No";
                        //get Change Component
                        var changeComponent = oContainerStatus.Attributes.Where(x => x.Name == "ChangeComponent").ToList();
                        _changeComponent = changeComponent.Count > 0 && changeComponent[0].AttributeValue == "True";
                        var changePcba = oContainerStatus.Attributes.Where(x => x.Name == "ChangePcba").ToList();
                        _changePcba = changePcba.Count > 0 && changePcba[0].AttributeValue == "True";
                        var changePump = oContainerStatus.Attributes.Where(x => x.Name == "ChangePump").ToList();
                        _changePump = changePump.Count > 0 && changePump[0].AttributeValue == "True";

                       
                        ppaScanBindingSource.Clear();
                        if (_changeComponent)
                        {
                            _scanlistSn = ppaScanBindingSource.Add(new PpaScan { ScanningList = "Appliacne S/N", Status = "Completed"});
                        }

                        if (_changePcba)
                        {
                            _scanlistPcba = new PpaScan {ScanningList = "PCBA QR Code"};
                               _scanlistPcbaIdx = ppaScanBindingSource.Add(_scanlistPcba);
                            var oldPcba = oContainerStatus.Attributes.Where(x => x.Name == "PcbaSn").ToList();
                            if (oldPcba.Count > 0)
                            {
                                _oldPcba = oldPcba[0].AttributeValue.Value;
                            }
                        }

                        if (_changePump)
                        {
                            _scanlistPump = new PpaScan {ScanningList = "Pump QR Code"};
                               _scanlistPumpIdx = ppaScanBindingSource.Add(_scanlistPump);
                            var oldPump = oContainerStatus.Attributes.Where(x => x.Name == "PumpSn").ToList();
                            if (oldPump.Count > 0)
                            {
                                _oldPump = oldPump[0].AttributeValue.Value;
                            }
                        }

                        kryptonDataGridView2.Visible = _changeComponent || _changePcba || _changePump;

                        if (oContainerStatus.MfgOrderName != null && _mesData.ManufacturingOrder == null || _mesData.ManufacturingOrder?.Name!= oContainerStatus.MfgOrderName)
                        {
                            if (oContainerStatus.MfgOrderName != null)
                            {
                                var mfg = await Mes.GetMfgOrder(_mesData, oContainerStatus.MfgOrderName.ToString());
                                _mesData.SetManufacturingOrder(mfg);
                            }

                            if (oContainerStatus.MfgOrderName != null)
                                Tb_PO.Text = oContainerStatus.MfgOrderName.ToString();
                            Tb_Product.Text = oContainerStatus.Product.Name;
                            Tb_ProductDesc.Text = oContainerStatus.ProductDescription.Value;
                            var img = await Mes.GetImage(_mesData, oContainerStatus.Product.Name);
                            pictureBox1.ImageLocation = img.Identifier.Value;

                            var cnt = await Mes.GetCounterFromMfgOrder(_mesData);
                            Tb_PpaQty.Text = cnt.ToString();
                        }

                        if (!_afterRepair)
                        {
                            _pcbaEnabled = _pcbaDataConfig.Enable == EnableDisable.Enable;
                            _pumpEnabled = _pumpDataConfig.Enable == EnableDisable.Enable;
                        }
                        else
                        {
                            _pcbaEnabled = _pcbaDataConfig.Enable == EnableDisable.Enable && _changePcba;
                            _pumpEnabled = _pumpDataConfig.Enable == EnableDisable.Enable && _changePump;
                        }


                        if (_pcbaEnabled && _pumpEnabled)
                        {
                            await SetPpaState(PPAState.ScanPcbaOrPumpSerialNumber);
                            break;
                        }

                        if (!_pcbaEnabled && !_pumpEnabled)
                        {
                            await SetPpaState(PPAState.UpdateMoveInMove);
                            break;
                        }

                        if (!_pcbaEnabled)
                        {
                            await SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        }

                        if (!_pumpEnabled)
                        {
                            await SetPpaState(PPAState.ScanPcbaSerialNumber);
                            break;
                        }
                        

                    }
                    var containerStep = await Mes.GetCurrentContainerStep(_mesData, Tb_SerialNumber.Text); // try get operation pos
                    if (containerStep != null && !_mesData.OperationName.Contains(containerStep))
                    {
                        _wrongOperationPosition = containerStep;
                        await SetPpaState(PPAState.WrongOperation);
                        break;
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
                                if (_pumpEnabled) listIssue.Add(_pumpData.ToIssueActualDetail());
                                if (_pcbaEnabled) listIssue.Add(_pcbaData.ToIssueActualDetail());
                                var consume = TransactionResult.Create(true);
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
                                        if (_pumpEnabled ||_pcbaEnabled)
                                        {
                                            var attrs = new List<ContainerAttrDetail>();
                                            if (_pumpEnabled)
                                            {
                                                attrs.AddRange(new[]
                                                {
                                                    new ContainerAttrDetail
                                                    {
                                                        Name = "PumpModel", AttributeValue = _pumpData.PartNumber.Value,
                                                        DataType = TrivialTypeEnum.String, IsExpression = false
                                                    },
                                                    new ContainerAttrDetail
                                                    {
                                                        Name = "PumpSn", AttributeValue = _pumpData.RawData,
                                                        DataType = TrivialTypeEnum.String, IsExpression = false
                                                    },
                                                });
                                            }
                                            if (_pcbaEnabled)
                                            {
                                                attrs.AddRange(new[]
                                                {
                                                    new ContainerAttrDetail
                                                    {
                                                        Name = Name = "PcbaSn" ,AttributeValue = _pcbaData.RawData,
                                                        DataType = TrivialTypeEnum.String, IsExpression = false
                                                    }
                                                });
                                            }
                                            await Mes.ExecuteContainerAttrMaint(_mesData,
                                                oContainerStatus, attrs.ToArray());
                                        }
                                        
                                        lbMoveOut.Text = _dbMoveOut.ToString(Mes.DateTimeStringFormat);
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
                                transPastDue = Mes.GetMaintenancePastDue(_mesData.MaintenanceStatusDetails);
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
                    lblCommand.Text = $@"Completed Scan, Container in {_wrongOperationPosition}";
                    break;
                case PPAState.ComponentNotFound:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Cannot Find Component in Bill of Material";
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
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Wait For Preparation";
                    btnStartPreparation.Enabled = true;
                    break;
                case PPAState.SamePcba:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Same PCBA, Please Replace with the New PCBA";
                    KryptonMessageBox.Show(this, "Same PCBA, Please Replace with the New PCBA", "Scan Pcba",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
                case PPAState.SamePump:
                    lblCommand.ForeColor = Color.Red;
                    _readScanner = false;
                    lblCommand.Text = @"Same Pump, Please Replace with the New Pump";
                    KryptonMessageBox.Show(this, "Same Pump, Please Replace with the New Pump", "Scan Pump",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            ppaScanBindingSource.Clear();
            kryptonDataGridView2.Visible = false;
        }

        #endregion

        #region FUNCTION STATUS OF RESOURCE

        private async Task GetStatusMaintenanceDetails()
        {
            try
            {
                var maintenanceStatusDetails = await Mes.GetMaintenanceStatusDetails(_mesData);
                _mesData.SetMaintenanceStatusDetails(maintenanceStatusDetails);
                if (maintenanceStatusDetails != null)
                {
                    getMaintenanceStatusDetailsBindingSource.DataSource =
                        new BindingList<GetMaintenanceStatusDetails>(maintenanceStatusDetails);
                    Dg_Maintenance.DataSource = getMaintenanceStatusDetailsBindingSource;

                    //get past due, warning, and tolerance
                    var pastDue = maintenanceStatusDetails.Where(x => x.MaintenanceState=="Past Due").ToList();
                    var due = maintenanceStatusDetails.Where(x => x.MaintenanceState == "Due").ToList();
                    var pending = maintenanceStatusDetails.Where(x => x.MaintenanceState == "Pending").ToList();

                    if (pastDue.Count > 0)
                    {
                        lblResMaintMesg.Text = @"Resource Maintenance Past Due";
                        lblResMaintMesg.BackColor = Color.Red;
                        lblResMaintMesg.Visible = true;
                        if (_mesData?.ResourceStatusDetails?.Reason?.Name != "Planned Maintenance")
                        {
                            await Mes.SetResourceStatus(_mesData, "PPA - Planned Downtime", "Planned Maintenance");
                        }
                        return;
                    }
                    if (due.Count > 0)
                    {
                        lblResMaintMesg.Text = @"Resource Maintenance Due";
                        lblResMaintMesg.BackColor = Color.Orange;
                        lblResMaintMesg.Visible = true;
                        return;
                    }
                    if (pending.Count > 0)
                    {
                        lblResMaintMesg.Text = @"Resource Maintenance Pending";
                        lblResMaintMesg.BackColor = Color.Yellow;
                        lblResMaintMesg.Visible = true;
                        return;
                    }
                }
                lblResMaintMesg.Visible = false;
                lblResMaintMesg.Text = "";
                getMaintenanceStatusDetailsBindingSource.DataSource = null;
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
        private readonly int _indexMaintenanceState=0;
        private bool _afterRepair = false;
        private bool _changeComponent;
        private bool _changePcba;
        private bool _changePump;
        private int _scanlistSn;
        private PpaScan _scanlistPcba;
        private PpaScan _scanlistPump;
        private string _oldPcba;
        private string _oldPump;
        private bool _pcbaEnabled;
        private bool _pumpEnabled;
        private int _scanlistPcbaIdx;
        private int _scanlistPumpIdx;
        private readonly Rs232Scanner _keyenceRs232Scanner;

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
                    case PPAState.ScanPumpSerialNumber:
                    case PPAState.ScanPcbaOrPumpSerialNumber:
                        var scannedPcba = Tb_Scanner.Text.Trim();
                        Tb_Scanner.Clear();
                        if (scannedPcba.IndexOf("135", StringComparison.Ordinal) == 0 && _pcbaEnabled)
                        {
                            var transactPcba = PcbaData.ParseData(scannedPcba, _pcbaDataConfig);
                            if (transactPcba.Result)
                            {
                                _pcbaData = (PcbaData)transactPcba.Data;
                                var s = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product?.Name == _pcbaData.PartNumber.Value).ToList();
                                if (s == null || s.Count == 0)
                                {
                                    await SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }

                                if (s[0].wikScanning!= "X")
                                {
                                    await SetPpaState(PPAState.WrongComponent);
                                    break;
                                }

                                if (_afterRepair)
                                {
                                    if (_oldPcba == scannedPcba)
                                    {
                                        await SetPpaState(PPAState.SamePcba);
                                        break;
                                    }
                                }
                                Tb_PCBAPartNumber.Text = _pcbaData.PartNumber?.Value;
                                Tb_PCBASerialNumber.Text = scannedPcba;
                                _pcbaData.SetQtyRequired(s[0].QtyRequired.Value);
                                if (_afterRepair && _scanlistPcba!=null)
                                {
                                    _scanlistPcba.Status = "Completed";
                                    ppaScanBindingSource[_scanlistPcbaIdx] = _scanlistPcba;
                                }
                                if (!_pumpEnabled || !string.IsNullOrEmpty(_pumpData.RawData))
                                {
                                    await SetPpaState(PPAState.UpdateMoveInMove);
                                    break;
                                }
                            }
                            else
                            {
                                var l = scannedPcba.Length > 10 ? 10 : scannedPcba.Length;
                                var str = scannedPcba.Substring(0, l);
                                var vPcba = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product.Name.Contains(str)).ToList();
                                if (vPcba == null || vPcba.Count == 0)
                                {
                                    await SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                await SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            await SetPpaState(PPAState.ScanPumpSerialNumber);
                            break;
                        }
                        if (scannedPcba.IndexOf("103", StringComparison.Ordinal) == 0 &&_pumpEnabled)
                        {
                            var scannedPump = scannedPcba;
                            var transactPump = PumpData.ParseData(scannedPump, _pumpDataConfig);
                            if (transactPump.Result)
                            {
                                _pumpData = (PumpData)transactPump.Data;
                                var s = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product?.Name == _pumpData.PartNumber.Value).ToList();
                                if (s.Count == 0)
                                {
                                    await SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                if (s[0].wikScanning != "X")
                                {
                                    await SetPpaState(PPAState.WrongComponent);
                                    break;
                                }
                                if (_afterRepair)
                                {
                                    if (_oldPump == scannedPump)
                                    {
                                        await SetPpaState(PPAState.SamePump);
                                        break;
                                    }
                                }
                                Tb_PumpPartNumber.Text = _pumpData.PartNumber?.Value;
                                Tb_PumpSerialNumber.Text = scannedPump;
                                _pumpData.SetQtyRequired(s[0].QtyRequired.Value);
                                if (_afterRepair && _scanlistPump != null)
                                {
                                    _scanlistPump.Status = "Completed";
                                    ppaScanBindingSource[_scanlistPumpIdx] = _scanlistPump;
                                }

                                if (!_pcbaEnabled || !string.IsNullOrEmpty(_pcbaData.RawData))
                                {
                                    await SetPpaState(PPAState.UpdateMoveInMove);
                                    break;
                                }
                            }
                            else
                            {
                                var l = scannedPump.Length > 10 ? 10 : scannedPump.Length;
                                var str = scannedPump.Substring(0, l);
                                var vPump = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product.Name.Contains(str)).ToList();
                                if (vPump == null || vPump.Count == 0)
                                {
                                    await SetPpaState(PPAState.ComponentNotFound);
                                    break;
                                }
                                await SetPpaState(PPAState.WrongComponent);
                                break;
                            }
                            await SetPpaState(PPAState.ScanPcbaSerialNumber);
                            break;
                        }
                        else
                        {
                            Tb_Scanner.Clear();
                            var l = scannedPcba.Length > 10 ? 10 : scannedPcba.Length;
                            var str = scannedPcba.Substring(0, l);
                            var vPcba = _mesData.ManufacturingOrder.MaterialList?.Where(x => x.Product.Name.Contains(str)).ToList();
                            if (vPcba == null || vPcba.Count == 0)
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
            ClearPo();
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
            try
            {
                _tempPcba.SaveToFile();
                _pcbaDataConfig = PcbaDataPointConfig.Load(PcbaDataPointConfig.FileName);
                KryptonMessageBox.Show("Pcba Setting Saved!");
            }
            catch
            {
                KryptonMessageBox.Show("Failed to save Pcba Setting");
            }
        }

        private void Btn_SetPump_Click(object sender, EventArgs e)
        {
            try
            {
                _tempPump.SaveToFile();
            _pumpDataConfig = PumpDataPointConfig.Load(PumpDataPointConfig.FileName);
            KryptonMessageBox.Show("Pump Setting Saved!");
            }
            catch
            {
                KryptonMessageBox.Show("Failed to save Pump Setting");
            }

        }
        private void kryptonNavigator1_Selecting(object sender, ComponentFactory.Krypton.Navigator.KryptonPageCancelEventArgs e)
        {
            if (e.Index != 1 && e.Index != 2) return;

            using (var ss = new LoginForm24(e.Index == 1 ? "Maintenance" : "Quality"))
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

      
     

        private void Tb_Scanner_TextChanged(object sender, EventArgs e)
        {

        }


        private void Dg_Maintenance_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            try
            {
                foreach (DataGridViewRow row in Dg_Maintenance.Rows)
                {
                    switch (Convert.ToString(row.Cells[_indexMaintenanceState].Value))
                    {
                        //Console.WriteLine(Convert.ToString(row.Cells["MaintenanceState"].Value));
                        case "Pending":
                            row.DefaultCellStyle.BackColor = Color.Yellow;
                            break;
                        case "Due":
                            row.DefaultCellStyle.BackColor = Color.Orange;
                            break;
                        case "Past Due":
                            row.DefaultCellStyle.BackColor = Color.Red;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Source = AppSettings.AssemblyName == ex.Source ? MethodBase.GetCurrentMethod()?.Name : MethodBase.GetCurrentMethod()?.Name + "." + ex.Source;
                EventLogUtil.LogErrorEvent(ex.Source, ex);
            }
        }

        private void label28_Click(object sender, EventArgs e)
        {

        }

        private void Tb_FinishedGoodCounter_TextChanged(object sender, EventArgs e)
        {

        }

        private async void btnFinishPreparation_Click(object sender, EventArgs e)
        {
            if (_mesData.ResourceStatusDetails == null) return;
            if (_mesData.ResourceStatusDetails.Reason.Name == "Maintenance") return;
            if (_mesData.ResourceStatusDetails?.Reason?.Name == "Planned Maintenance") return;
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
            if (_mesData.ResourceStatusDetails?.Reason?.Name == "Planned Maintenance") return;
            _mesData.SetManufacturingOrder(null);
            var result = await Mes.SetResourceStatus(_mesData, "PPA - Planned Downtime", "Preparation");
            await GetStatusOfResource();
            if (result)
            {
                await SetPpaState(PPAState.WaitPreparation);
                btnFinishPreparation.Enabled = true;
                btnStartPreparation.Enabled = false;
            }
        }
    }
}
