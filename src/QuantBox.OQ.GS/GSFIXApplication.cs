using System;
using System.Collections.Generic;

using SmartQuant.FIX;
using SmartQuant.FIXApplication;
using QuickFix;
using SmartQuant.Execution;
using SmartQuant.Instruments;
using SmartQuant;
using System.Collections;

namespace QuantBox.OQ.GS
{
    class GSFIXApplication : QuickFIX42CommonApplication
    {
        // Class members
        private Hashtable cancelRequests = new Hashtable();


        public GSFIXApplication(GSFIX provider)
            : base(provider,"GS_FIX")
        {
        }

        public override void toAdmin(QuickFix.Message message, QuickFix.SessionID sessionID)
        {
            base.toAdmin(message, sessionID);

            if (message is QuickFix42.Logon)
            {
                message.setField(new ResetSeqNumFlag(true));

                GSFIX gsfix = provider as GSFIX;

                message.setField(new RawData(string.Format("Z:{0}:{1}:",gsfix.Account,gsfix.Password)));
                message.setField(new EncryptMethod(EncryptMethod.NONE));
            }
        }

        public override void fromAdmin(Message message, SessionID sessionID)
        {
            base.fromAdmin(message, sessionID);

            if ((message is QuickFix42.Logout || message is QuickFix42.Reject) && message.isSetField(QuickFix.Text.FIELD))
            {
                Console.WriteLine(message.getString(QuickFix.Text.FIELD));
            }
        }

        public override void onMessage(QuickFix42.BusinessMessageReject message, SessionID session)
        {
            string text = (message.isSetText()) ? message.getText().getValue() : null;

            if (text != null)
                provider.EmitError(text);
        }

        public override void onMessage(QuickFix42.OrderCancelReject message, SessionID session)
        {
            OrderCancelReject reject = new OrderCancelReject();

            // required fields
            reject.TransactTime = Clock.Now;
            reject.OrderID = message.getOrderID().getValue();
            reject.ClOrdID = message.getClOrdID().getValue();
            reject.OrigClOrdID = message.getOrigClOrdID().getValue();

            (reject as FIXOrderCancelReject).OrdStatus = message.getOrdStatus().getValue();
            (reject as FIXOrderCancelReject).CxlRejResponseTo = message.getCxlRejResponseTo().getValue();
            (reject as FIXOrderCancelReject).CxlRejReason = message.getCxlRejReason().getValue();

            // optional fields
            if (message.isSetSecondaryOrderID())
                reject.SecondaryOrderID = message.getSecondaryOrderID().getValue();

            if (message.isSetAccount())
                reject.Account = message.getAccount().getValue();

            if (message.isSetText())
                reject.Text = message.getText().getValue();

            // event
            provider.EmitOrderCancelReject(reject);
        }

        public override void onMessage(QuickFix42.ExecutionReport report, QuickFix.SessionID sessionID)
        {
            if (report.getExecType().getValue() == QuickFix.ExecType.PENDING_CANCEL ||
                report.getExecType().getValue() == QuickFix.ExecType.CANCELED ||
                report.getExecType().getValue() == QuickFix.ExecType.PENDING_REPLACE ||
                report.getExecType().getValue() == QuickFix.ExecType.REPLACE)
            {
                object request = cancelRequests[report.getClOrdID().getValue()];

                if (request == null)
                    report.set(new OrigClOrdID(report.getClOrdID().getValue()));
                else
                {
                    if (request is FIXOrderCancelRequest)
                        report.set(new OrigClOrdID((request as FIXOrderCancelRequest).OrigClOrdID));

                    if (request is FIXOrderCancelReplaceRequest)
                        report.set(new OrigClOrdID((request as FIXOrderCancelReplaceRequest).OrigClOrdID));
                }
            }

            ExecutionReport Report = new ExecutionReport();

            if (report.isSetOrderID()) Report.OrderID = report.getOrderID().getValue();
            ////if (report.isSetSecondaryOrderID()) Report.SecondaryOrderID = report.getSecondaryOrderID().getValue();
            if (report.isSetClOrdID()) Report.ClOrdID = report.getClOrdID().getValue();
            if (report.isSetOrigClOrdID()) Report.OrigClOrdID = report.getOrigClOrdID().getValue();
            ////if (report.isSetListID()) Report.ListID = report.getListID().getValue();
            if (report.isSetExecID()) Report.ExecID = report.getExecID().getValue();
            ////if (report.isSetExecRefID()) Report.ExecRefID = report.getExecRefID().getValue();
            if (report.isSetExecType()) (Report as FIXExecutionReport).ExecType = report.getExecType().getValue();
            if (report.isSetOrdStatus()) (Report as FIXExecutionReport).OrdStatus = report.getOrdStatus().getValue();
            if (report.isSetOrdRejReason()) Report.OrdRejReason = report.getOrdRejReason().getValue();
            ////if (report.isSetExecRestatementReason()) Report.ExecRestatementReason = report.getExecRestatementReason().getValue();
            ////if (report.isSetAccount()) Report.Account = report.getAccount().getValue();
            ////if (report.isSetSettlmntTyp()) Report.SettlType = report.getSettlmntTyp().getValue();
            //if (report.isSetFutSettDate           ()) Report.FutSettDate            = report.getFutSettDate           ().getValue();
            if (report.isSetSymbol()) Report.Symbol = report.getSymbol().getValue();
            ////if (report.isSetSymbolSfx()) Report.SymbolSfx = report.getSymbolSfx().getValue();
            ////if (report.isSetSecurityID()) Report.SecurityID = report.getSecurityID().getValue();
            //if (report.isSetIDSource              ()) Report.IDSource               = report.getIDSource              ().getValue();
            ////if (report.isSetSecurityType()) Report.SecurityType = report.getSecurityType().getValue();
            ////if (report.isSetMaturityMonthYear()) Report.MaturityMonthYear = report.getMaturityMonthYear().getValue();
            //if (report.isSetMaturityDay           ()) Report.MaturityDate           = DateTime.Parse(report.getMaturityDay           ().getValue());
            //if (report.isSetPutOrCall             ()) Report.PutOrCall              = report.getPutOrCall             ().getValue();
            ////if (report.isSetStrikePrice()) Report.StrikePrice = report.getStrikePrice().getValue();
            ////if (report.isSetOptAttribute()) Report.OptAttribute = report.getOptAttribute().getValue();
            ////if (report.isSetContractMultiplier()) Report.ContractMultiplier = report.getContractMultiplier().getValue();
            ////if (report.isSetCouponRate()) Report.CouponRate = report.getCouponRate().getValue();
            ////if (report.isSetSecurityExchange()) Report.SecurityExchange = report.getSecurityExchange().getValue();
            ////if (report.isSetIssuer()) Report.Issuer = report.getIssuer().getValue();
            ////if (report.isSetEncodedIssuerLen()) Report.EncodedIssuerLen = report.getEncodedIssuerLen().getValue();
            ////if (report.isSetEncodedIssuer()) Report.EncodedIssuer = report.getEncodedIssuer().getValue();
            ////if (report.isSetSecurityDesc()) Report.SecurityDesc = report.getSecurityDesc().getValue();
            ////if (report.isSetEncodedSecurityDescLen()) Report.EncodedSecurityDescLen = report.getEncodedSecurityDescLen().getValue();
            ////if (report.isSetEncodedSecurityDesc()) Report.EncodedSecurityDesc = report.getEncodedSecurityDesc().getValue();
            if (report.isSetSide()) (Report as FIXExecutionReport).Side = report.getSide().getValue();
            if (report.isSetOrderQty()) Report.OrderQty = report.getOrderQty().getValue();
            ////if (report.isSetCashOrderQty()) Report.CashOrderQty = report.getCashOrderQty().getValue();
            if (report.isSetOrdType()) (Report as FIXExecutionReport).OrdType = report.getOrdType().getValue();
            if (report.isSetPrice()) Report.Price = report.getPrice().getValue();
            ////if (report.isSetStopPx()) Report.StopPx = report.getStopPx().getValue();
            //if (report.isSetPegDifference         ()) Report.PegDifference          = report.getPegDifference         ().getValue();		
            ////if (report.isSetDiscretionInst()) Report.DiscretionInst = report.getDiscretionInst().getValue();
            ////if (report.isSetDiscretionOffset()) Report.DiscretionOffsetValue = report.getDiscretionOffset().getValue();
            ////if (report.isSetCurrency()) Report.Currency = report.getCurrency().getValue();
            ////if (report.isSetComplianceID()) Report.ComplianceID = report.getComplianceID().getValue();
            //if (report.isSetSolicitedFlag         ()) Report.SolicitedFlag          = report.getSolicitedFlag         ().getValue();
            ////if (report.isSetTimeInForce()) (Report as FIXExecutionReport).TimeInForce = report.getTimeInForce().getValue();
            ////if (report.isSetEffectiveTime()) Report.EffectiveTime = report.getEffectiveTime().getValue();
            ////if (report.isSetExpireDate()) Report.ExpireDate = DateTime.Parse(report.getExpireDate().getValue());
            ////if (report.isSetExpireTime()) Report.ExpireTime = report.getExpireTime().getValue();
            ////if (report.isSetExecInst()) Report.ExecInst = report.getExecInst().getValue();
            //if (report.isSetRule80A               ()) Report.Rule80A                = report.getRule80A               ().getValue();
            if (report.isSetLastShares()) Report.LastQty = report.getLastShares().getValue();
            if (report.isSetLastPx()) Report.LastPx = report.getLastPx().getValue();
            ////if (report.isSetLastSpotRate()) Report.LastSpotRate = report.getLastSpotRate().getValue();
            ////if (report.isSetLastForwardPoints()) Report.LastForwardPoints = report.getLastForwardPoints().getValue();
            ////if (report.isSetLastMkt()) Report.LastMkt = report.getLastMkt().getValue();
            ////if (report.isSetTradingSessionID()) Report.TradingSessionID = report.getTradingSessionID().getValue();
            ////if (report.isSetLastCapacity()) Report.LastCapacity = report.getLastCapacity().getValue();
            if (report.isSetLeavesQty()) Report.LeavesQty = report.getLeavesQty().getValue();
            if (report.isSetCumQty()) Report.CumQty = report.getCumQty().getValue();
            if (report.isSetAvgPx()) Report.AvgPx = report.getAvgPx().getValue();
            ////if (report.isSetDayOrderQty()) Report.DayOrderQty = report.getDayOrderQty().getValue();
            ////if (report.isSetDayCumQty()) Report.DayCumQty = report.getDayCumQty().getValue();
            ////if (report.isSetDayAvgPx()) Report.DayAvgPx = report.getDayAvgPx().getValue();
            ////if (report.isSetGTBookingInst()) Report.GTBookingInst = report.getGTBookingInst().getValue();
            ////if (report.isSetTradeDate()) Report.TradeDate = DateTime.Parse(report.getTradeDate().getValue());
            if (report.isSetTransactTime()) Report.TransactTime = report.getTransactTime().getValue();
            //if (report.isSetReportToExch          ()) Report.ReportToExch           = report.getReportToExch          ().getValue();
            ////if (report.isSetCommission()) Report.Commission = report.getCommission().getValue();
            ////if (report.isSetCommType()) (Report as FIXExecutionReport).CommType = report.getCommType().getValue();
            ////if (report.isSetGrossTradeAmt()) Report.GrossTradeAmt = report.getGrossTradeAmt().getValue();
            ////if (report.isSetSettlCurrAmt()) Report.SettlCurrAmt = report.getSettlCurrAmt().getValue();
            ////if (report.isSetSettlCurrency()) Report.SettlCurrency = report.getSettlCurrency().getValue();
            ////if (report.isSetHandlInst()) Report.HandlInst = report.getHandlInst().getValue();
            ////if (report.isSetMinQty()) Report.MinQty = report.getMinQty().getValue();
            ////if (report.isSetMaxFloor()) Report.MaxFloor = report.getMaxFloor().getValue();
            //if (report.isSetOpenClose             ()) Report.OpenClose              = report.getOpenClose             ().getValue();
            ////if (report.isSetMaxShow()) Report.MaxShow = report.getMaxShow().getValue();
            if (report.isSetText()) Report.Text = report.getText().getValue();
            ////if (report.isSetEncodedTextLen()) Report.EncodedTextLen = report.getEncodedTextLen().getValue();
            ////if (report.isSetEncodedText()) Report.EncodedText = report.getEncodedText().getValue();
            //if (report.isSetFutSettDate2          ()) Report.FutSettDate2           = report.getFutSettDate2          ().getValue();
            ////if (report.isSetOrderQty2()) Report.OrderQty2 = report.getOrderQty2().getValue();
            //if (report.isSetClearingFirm          ()) Report.ClearingFirm           = report.getClearingFirm          ().getValue();
            //if (report.isSetClearingAccount       ()) Report.ClearingAccount        = report.getClearingAccount       ().getValue();
            ////if (report.isSetMultiLegReportingType()) Report.MultiLegReportingType = report.getMultiLegReportingType().getValue();

            //

            SingleOrder order;

            if (Report.ExecType == SmartQuant.FIX.ExecType.PendingCancel ||
                Report.ExecType == SmartQuant.FIX.ExecType.Cancelled ||
                Report.ExecType == SmartQuant.FIX.ExecType.PendingReplace ||
                Report.ExecType == SmartQuant.FIX.ExecType.Replace)

                order = OrderManager.Orders.All[Report.OrigClOrdID] as SingleOrder;
            else
                order = OrderManager.Orders.All[Report.ClOrdID] as SingleOrder;

            Instrument instrument = order.Instrument;

            Report.Symbol = instrument.Symbol;

            Report.TransactTime = Clock.Now;

            // emit execution report

            EmitExecutionReport(Report);
        }

        public override void Send(FIXNewOrderSingle Order)
        {
            QuickFix42.NewOrderSingle order = new QuickFix42.NewOrderSingle(
                new QuickFix.ClOrdID(Order.ClOrdID),
                //new QuickFix.HandlInst(Order.HandlInst),
                new QuickFix.HandlInst(HandlInst.AUTOEXECPRIV), // GS FIX
                new QuickFix.Symbol(Order.Symbol),
                new QuickFix.Side(Order.Side),
                new QuickFix.TransactTime(Order.TransactTime),
                new QuickFix.OrdType(Order.OrdType));

            if ((Order.OrdType == FIXOrdType.Limit || Order.OrdType == FIXOrdType.StopLimit) && Order.ContainsField(EFIXField.Price))
                order.set(new QuickFix.Price(Order.Price));

            if ((Order.OrdType == FIXOrdType.Stop || Order.OrdType == FIXOrdType.StopLimit) && Order.ContainsField(EFIXField.StopPx))
                order.set(new QuickFix.StopPx(Order.StopPx));

            // 自己计算交易所
            if (Order.Symbol.StartsWith("60") || Order.Symbol.StartsWith("51"))
            {
                //order.set(new QuickFix.SecurityExchange("XSHG"));// 上海
                order.set(new QuickFix.SecurityExchange("SS"));// 上海
            }
            else if (Order.Symbol.StartsWith("00") || Order.Symbol.StartsWith("30") || Order.Symbol.StartsWith("15"))
            {
                //order.set(new QuickFix.SecurityExchange("XSHE"));// 深圳
                order.set(new QuickFix.SecurityExchange("SZ"));// 深圳
            }

            //order.set(new QuickFix.SecurityType(Order.SecurityType));
            order.set(new QuickFix.OrderQty(Order.OrderQty));
            //order.set(new QuickFix.Account(Order.Account));
            //order.set(new QuickFix.Rule80A(Order.Rule80A));
            //order.set(new QuickFix.CustomerOrFirm(Order.CustomerOrFirm));

            //if (Order.ContainsField(EFIXField.ClearingAccount))
            //    order.set(new QuickFix.ClearingAccount(Order.ClearingAccount));

            order.set(new QuickFix.Currency("CNY")); // GS FIX

            Session.sendToTarget(order, orderSessionID);
        }

        public override void Send(FIXOrderCancelRequest Request)
        {
            QuickFix42.OrderCancelRequest request = new QuickFix42.OrderCancelRequest(
                new QuickFix.OrigClOrdID(Request.OrigClOrdID),
                new QuickFix.ClOrdID(Request.ClOrdID),
                new QuickFix.Symbol(Request.Symbol),
                new QuickFix.Side(Request.Side),
                new QuickFix.TransactTime(Request.TransactTime));

            // instrument component block

            //request.set(new QuickFix.SecurityType(Request.SecurityType));
            //request.set(new QuickFix.SecurityID(Request.SecurityID));
            //request.set(new QuickFix.SecurityExchange(Request.SecurityExchange));
            //request.set(new QuickFix.Account(Request.Account));

            request.set(new QuickFix.OrderQty(0)); //GS FIX

            cancelRequests.Add(Request.ClOrdID, Request);

            Session.sendToTarget(request, orderSessionID);
        }
    }
}
