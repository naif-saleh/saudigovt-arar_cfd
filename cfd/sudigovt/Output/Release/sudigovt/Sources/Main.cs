using CallFlow.CFD;
using CallFlow;
using MimeKit;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;
using System.Threading;
using System;
using TCX.Configuration;

namespace sudigovt
{
    public class Main : ScriptBase<Main>, ICallflow, ICallflowProcessor
    {
        private bool executionStarted;
        private bool executionFinished;
        private bool disconnectFlowPending;

        private BufferBlock<AbsEvent> eventBuffer;

        private int currentComponentIndex;
        private List<AbsComponent> mainFlowComponentList;
        private List<AbsComponent> disconnectFlowComponentList;
        private List<AbsComponent> errorFlowComponentList;
        private List<AbsComponent> currentFlowComponentList;

        private LogFormatter logFormatter;
        private TimerManager timerManager;
        private Dictionary<string, Variable> variableMap;
        private TempWavFileManager tempWavFileManager;
        private PromptQueue promptQueue;
        private OnlineServices onlineServices;

        private void DisconnectCallAndExitCallflow()
        {
            if (currentFlowComponentList == disconnectFlowComponentList)
                logFormatter.Trace("Callflow finished...");
            else
            {
                logFormatter.Trace("Callflow finished, disconnecting call...");
                MyCall.Terminate();
            }
        }

        private async Task ExecuteErrorFlow()
        {
            if (currentFlowComponentList == errorFlowComponentList)
            {
                logFormatter.Trace("Error during error handler flow, exiting callflow...");
                DisconnectCallAndExitCallflow();
            }
            else if (currentFlowComponentList == disconnectFlowComponentList)
            {
                logFormatter.Trace("Error during disconnect handler flow, exiting callflow...");
                executionFinished = true;
            }
            else
            {
                currentFlowComponentList = errorFlowComponentList;
                currentComponentIndex = 0;
                if (errorFlowComponentList.Count > 0)
                {
                    logFormatter.Trace("Start executing error handler flow...");
                    await ProcessStart();
                }
                else
                {
                    logFormatter.Trace("Error handler flow is empty...");
                    DisconnectCallAndExitCallflow();
                }
            }
        }

        private async Task ExecuteDisconnectFlow()
        {
            currentFlowComponentList = disconnectFlowComponentList;
            currentComponentIndex = 0;
            disconnectFlowPending = false;
            if (disconnectFlowComponentList.Count > 0)
            {
                logFormatter.Trace("Start executing disconnect handler flow...");
                await ProcessStart();
            }
            else
            {
                logFormatter.Trace("Disconnect handler flow is empty...");
                executionFinished = true;
            }
        }

        private EventResults CheckEventResult(EventResults eventResult)
        {
            if (eventResult == EventResults.MoveToNextComponent && ++currentComponentIndex == currentFlowComponentList.Count)
            {
                DisconnectCallAndExitCallflow();
                return EventResults.Exit;
            }
            else if (eventResult == EventResults.Exit)
                DisconnectCallAndExitCallflow();

            return eventResult;
        }

        private void InitializeVariables(string callID)
        {
            // Call variables
            variableMap["session.ani"] = new Variable(MyCall.Caller.CallerID);
            variableMap["session.callid"] = new Variable(callID);
            variableMap["session.dnis"] = new Variable(MyCall.DN.Number);
            variableMap["session.did"] = new Variable(MyCall.Caller.CalledNumber);
            variableMap["session.audioFolder"] = new Variable(Path.Combine(RecordingManager.Instance.AudioFolder, promptQueue.ProjectAudioFolder));
            variableMap["session.transferingExtension"] = new Variable(MyCall["onbehlfof"] ?? string.Empty);

            // Standard variables
            variableMap["RecordResult.NothingRecorded"] = new Variable(RecordComponent.RecordResults.NothingRecorded);
            variableMap["RecordResult.StopDigit"] = new Variable(RecordComponent.RecordResults.StopDigit);
            variableMap["RecordResult.Completed"] = new Variable(RecordComponent.RecordResults.Completed);
            variableMap["MenuResult.Timeout"] = new Variable(MenuComponent.MenuResults.Timeout);
            variableMap["MenuResult.InvalidOption"] = new Variable(MenuComponent.MenuResults.InvalidOption);
            variableMap["MenuResult.ValidOption"] = new Variable(MenuComponent.MenuResults.ValidOption);
            variableMap["UserInputResult.Timeout"] = new Variable(UserInputComponent.UserInputResults.Timeout);
            variableMap["UserInputResult.InvalidDigits"] = new Variable(UserInputComponent.UserInputResults.InvalidDigits);
            variableMap["UserInputResult.ValidDigits"] = new Variable(UserInputComponent.UserInputResults.ValidDigits);
            variableMap["VoiceInputResult.Timeout"] = new Variable(VoiceInputComponent.VoiceInputResults.Timeout);
            variableMap["VoiceInputResult.InvalidInput"] = new Variable(VoiceInputComponent.VoiceInputResults.InvalidInput);
            variableMap["VoiceInputResult.ValidInput"] = new Variable(VoiceInputComponent.VoiceInputResults.ValidInput);
            variableMap["VoiceInputResult.ValidDtmfInput"] = new Variable(VoiceInputComponent.VoiceInputResults.ValidDtmfInput);

            // User variables
            variableMap["callflow$.nid"] = new Variable("");
            variableMap["callflow$.otp"] = new Variable("");
            variableMap["callflow$.rid"] = new Variable("");
            variableMap["callflow$.nloop"] = new Variable(0);
            variableMap["callflow$.rloop"] = new Variable(0);
            variableMap["callflow$.oloop"] = new Variable(0);
            variableMap["callflow$.statusid"] = new Variable("");
            variableMap["callflow$.departmentid"] = new Variable("");
            variableMap["callflow$.counter"] = new Variable(0);
            variableMap["callflow$.otpCLoop"] = new Variable(0);
            variableMap["callflow$.rateTrue"] = new Variable(1);
            variableMap["callflow$.mobileNo"] = new Variable("");
            variableMap["callflow$.otpError"] = new Variable("");
            variableMap["callflow$.response"] = new Variable("");
            variableMap["RecordResult.NothingRecorded"] = new Variable(RecordComponent.RecordResults.NothingRecorded);
            variableMap["RecordResult.StopDigit"] = new Variable(RecordComponent.RecordResults.StopDigit);
            variableMap["RecordResult.Completed"] = new Variable(RecordComponent.RecordResults.Completed);
            variableMap["MenuResult.Timeout"] = new Variable(MenuComponent.MenuResults.Timeout);
            variableMap["MenuResult.InvalidOption"] = new Variable(MenuComponent.MenuResults.InvalidOption);
            variableMap["MenuResult.ValidOption"] = new Variable(MenuComponent.MenuResults.ValidOption);
            variableMap["UserInputResult.Timeout"] = new Variable(UserInputComponent.UserInputResults.Timeout);
            variableMap["UserInputResult.InvalidDigits"] = new Variable(UserInputComponent.UserInputResults.InvalidDigits);
            variableMap["UserInputResult.ValidDigits"] = new Variable(UserInputComponent.UserInputResults.ValidDigits);
            variableMap["VoiceInputResult.Timeout"] = new Variable(VoiceInputComponent.VoiceInputResults.Timeout);
            variableMap["VoiceInputResult.InvalidInput"] = new Variable(VoiceInputComponent.VoiceInputResults.InvalidInput);
            variableMap["VoiceInputResult.ValidInput"] = new Variable(VoiceInputComponent.VoiceInputResults.ValidInput);
            variableMap["VoiceInputResult.ValidDtmfInput"] = new Variable(VoiceInputComponent.VoiceInputResults.ValidDtmfInput);
            
        }

        private void InitializeComponents(ICallflow callflow, ICall myCall, string logHeader)
        {
            {
            LoopComponent Loop2 = new LoopComponent("Loop2", callflow, myCall, logHeader);
            Loop2.Condition = () => { return Convert.ToBoolean(CFDFunctions.LESS_THAN((IComparable)variableMap["callflow$.nloop"].Value,(IComparable)3)); };
            Loop2.Container = new SequenceContainerComponent("Loop2_Container", callflow, myCall, logHeader);
            mainFlowComponentList.Add(Loop2);
            UserInputComponent getnationalid = new UserInputComponent("getnationalid", callflow, myCall, logHeader);
            getnationalid.AllowDtmfInput = true;
            getnationalid.MaxRetryCount = 2;
            getnationalid.FirstDigitTimeout = 10000;
            getnationalid.InterDigitTimeout = 3000;
            getnationalid.FinalDigitTimeout = 3000;
            getnationalid.MinDigits = 10;
            getnationalid.MaxDigits = 20;
            getnationalid.ValidDigitList.AddRange(new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
            getnationalid.StopDigitList.AddRange(new char[] { '#' });
            getnationalid.InitialPrompts.Add(new AudioFilePrompt(() => { return "converted_new_enter_id.wav"; }));
            getnationalid.SubsequentPrompts.Add(new AudioFilePrompt(() => { return "converted_try_enter_id.wav"; }));
            getnationalid.InvalidDigitPrompts.Add(new AudioFilePrompt(() => { return "converted_wrong_id.wav"; }));
            getnationalid.TimeoutPrompts.Add(new AudioFilePrompt(() => { return "converted_sorrr_didnt_enter_id.wav"; }));
            Loop2.Container.ComponentList.Add(getnationalid);
            ConditionalComponent getnationalid_Conditional = new ConditionalComponent("getnationalid_Conditional", callflow, myCall, logHeader);
            Loop2.Container.ComponentList.Add(getnationalid_Conditional);
            getnationalid_Conditional.ConditionList.Add(() => { return getnationalid.Result == UserInputComponent.UserInputResults.ValidDigits; });
            getnationalid_Conditional.ContainerList.Add(new SequenceContainerComponent("getnationalid_Conditional_ValidInput", callflow, myCall, logHeader));
            VariableAssignmentComponent Assign_nationid = new VariableAssignmentComponent("Assign_nationid", callflow, myCall, logHeader);
            Assign_nationid.VariableName = "callflow$.nid";
            Assign_nationid.VariableValueHandler = () => { return getnationalid.Buffer; };
            getnationalid_Conditional.ContainerList[0].ComponentList.Add(Assign_nationid);
            WebInteractionComponent NationNoResponse = new WebInteractionComponent("NationNoResponse", callflow, myCall, logHeader);
            NationNoResponse.HttpMethod = System.Net.Http.HttpMethod.Post;
            NationNoResponse.ContentType = "application/json";
            NationNoResponse.Timeout = 30000;
            NationNoResponse.UriHandler = () => { return Convert.ToString("https://tabuk.ecolor.com.sa/mock_server.php/api/checkRequestNationalNo"); };
            NationNoResponse.ContentHandler = () => { return Convert.ToString(CFDFunctions.CONCATENATE(Convert.ToString("{\"NationalNo\":"),Convert.ToString(variableMap["callflow$.nid"].Value),Convert.ToString("}"))); };
            getnationalid_Conditional.ContainerList[0].ComponentList.Add(NationNoResponse);
            ConditionalComponent CreateCondition3 = new ConditionalComponent("CreateCondition3", callflow, myCall, logHeader);
            getnationalid_Conditional.ContainerList[0].ComponentList.Add(CreateCondition3);
            CreateCondition3.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(NationNoResponse.ResponseContent,"true")); });
            CreateCondition3.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch13", callflow, myCall, logHeader));
            VariableAssignmentComponent exitLoop = new VariableAssignmentComponent("exitLoop", callflow, myCall, logHeader);
            exitLoop.VariableName = "callflow$.nloop";
            exitLoop.VariableValueHandler = () => { return 3; };
            CreateCondition3.ContainerList[0].ComponentList.Add(exitLoop);
            CreateCondition3.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.NOT_EQUAL(NationNoResponse.ResponseContent,"true")); });
            CreateCondition3.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch14", callflow, myCall, logHeader));
            PromptPlaybackComponent PromptPlayback3 = new PromptPlaybackComponent("PromptPlayback3", callflow, myCall, logHeader);
            PromptPlayback3.AllowDtmfInput = true;
            PromptPlayback3.Prompts.Add(new AudioFilePrompt(() => { return "converted_record_didnt_find.wav"; }));
            CreateCondition3.ContainerList[1].ComponentList.Add(PromptPlayback3);
            IncrementVariableComponent IncrementVariable4 = new IncrementVariableComponent("IncrementVariable4", callflow, myCall, logHeader);
            IncrementVariable4.VariableName = "callflow$.nloop";
            CreateCondition3.ContainerList[1].ComponentList.Add(IncrementVariable4);
            ConditionalComponent CreateCondition5 = new ConditionalComponent("CreateCondition5", callflow, myCall, logHeader);
            CreateCondition3.ContainerList[1].ComponentList.Add(CreateCondition5);
            CreateCondition5.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.GREAT_THAN_OR_EQUAL((IComparable)variableMap["callflow$.nloop"].Value,(IComparable)3)); });
            CreateCondition5.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch12", callflow, myCall, logHeader));
            ExitComponent ExitCallflow6 = new ExitComponent("ExitCallflow6", callflow, myCall, logHeader);
            CreateCondition5.ContainerList[0].ComponentList.Add(ExitCallflow6);
            getnationalid_Conditional.ConditionList.Add(() => { return getnationalid.Result == UserInputComponent.UserInputResults.InvalidDigits || getnationalid.Result == UserInputComponent.UserInputResults.Timeout; });
            getnationalid_Conditional.ContainerList.Add(new SequenceContainerComponent("getnationalid_Conditional_InvalidInput", callflow, myCall, logHeader));
            ExitComponent endCall = new ExitComponent("endCall", callflow, myCall, logHeader);
            getnationalid_Conditional.ContainerList[1].ComponentList.Add(endCall);
            LoopComponent recordvalidNo = new LoopComponent("recordvalidNo", callflow, myCall, logHeader);
            recordvalidNo.Condition = () => { return Convert.ToBoolean(CFDFunctions.LESS_THAN((IComparable)variableMap["callflow$.rloop"].Value,(IComparable)3)); };
            recordvalidNo.Container = new SequenceContainerComponent("recordvalidNo_Container", callflow, myCall, logHeader);
            mainFlowComponentList.Add(recordvalidNo);
            UserInputComponent getrecordid = new UserInputComponent("getrecordid", callflow, myCall, logHeader);
            getrecordid.AllowDtmfInput = true;
            getrecordid.MaxRetryCount = 2;
            getrecordid.FirstDigitTimeout = 10000;
            getrecordid.InterDigitTimeout = 3000;
            getrecordid.FinalDigitTimeout = 3000;
            getrecordid.MinDigits = 4;
            getrecordid.MaxDigits = 7;
            getrecordid.ValidDigitList.AddRange(new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
            getrecordid.StopDigitList.AddRange(new char[] { '#' });
            getrecordid.InitialPrompts.Add(new AudioFilePrompt(() => { return "converted_new_record_id.wav"; }));
            getrecordid.SubsequentPrompts.Add(new AudioFilePrompt(() => { return "converted_try_enter_record.wav"; }));
            getrecordid.InvalidDigitPrompts.Add(new AudioFilePrompt(() => { return "converted_wrong_record.wav"; }));
            getrecordid.TimeoutPrompts.Add(new AudioFilePrompt(() => { return "converted_sorry_didnt_enter_record.wav"; }));
            recordvalidNo.Container.ComponentList.Add(getrecordid);
            ConditionalComponent getrecordid_Conditional = new ConditionalComponent("getrecordid_Conditional", callflow, myCall, logHeader);
            recordvalidNo.Container.ComponentList.Add(getrecordid_Conditional);
            getrecordid_Conditional.ConditionList.Add(() => { return getrecordid.Result == UserInputComponent.UserInputResults.ValidDigits; });
            getrecordid_Conditional.ContainerList.Add(new SequenceContainerComponent("getrecordid_Conditional_ValidInput", callflow, myCall, logHeader));
            VariableAssignmentComponent Assign_recordid = new VariableAssignmentComponent("Assign_recordid", callflow, myCall, logHeader);
            Assign_recordid.VariableName = "callflow$.rid";
            Assign_recordid.VariableValueHandler = () => { return getrecordid.Buffer; };
            getrecordid_Conditional.ContainerList[0].ComponentList.Add(Assign_recordid);
            WebInteractionComponent getRequestInfo = new WebInteractionComponent("getRequestInfo", callflow, myCall, logHeader);
            getRequestInfo.HttpMethod = System.Net.Http.HttpMethod.Post;
            getRequestInfo.ContentType = "application/json";
            getRequestInfo.Timeout = 60000;
            getRequestInfo.UriHandler = () => { return Convert.ToString("https://tabuk.ecolor.com.sa/mock_server.php/api/GetRequestInfo"); };
            getRequestInfo.ContentHandler = () => { return Convert.ToString(CFDFunctions.CONCATENATE(Convert.ToString("{\"NationalNo\":"),Convert.ToString(variableMap["callflow$.nid"].Value),Convert.ToString(",\"RecordNo\":"),Convert.ToString(variableMap["callflow$.rid"].Value),Convert.ToString("}"))); };
            getrecordid_Conditional.ContainerList[0].ComponentList.Add(getRequestInfo);
            VariableAssignmentComponent AssignVariable1 = new VariableAssignmentComponent("AssignVariable1", callflow, myCall, logHeader);
            AssignVariable1.VariableName = "callflow$.response";
            AssignVariable1.VariableValueHandler = () => { return getRequestInfo.ResponseContent; };
            getrecordid_Conditional.ContainerList[0].ComponentList.Add(AssignVariable1);
            ConditionalComponent CreateCondition1 = new ConditionalComponent("CreateCondition1", callflow, myCall, logHeader);
            getrecordid_Conditional.ContainerList[0].ComponentList.Add(CreateCondition1);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["callflow$.response"].Value,"Identity No. or Record No. is invalid")); });
            CreateCondition1.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch3", callflow, myCall, logHeader));
            PromptPlaybackComponent PromptPlayback1 = new PromptPlaybackComponent("PromptPlayback1", callflow, myCall, logHeader);
            PromptPlayback1.AllowDtmfInput = true;
            PromptPlayback1.Prompts.Add(new AudioFilePrompt(() => { return "converted_record_didnt_find.wav"; }));
            CreateCondition1.ContainerList[1].ComponentList.Add(PromptPlayback1);
            ConditionalComponent CreateCondition2 = new ConditionalComponent("CreateCondition2", callflow, myCall, logHeader);
            CreateCondition1.ContainerList[1].ComponentList.Add(CreateCondition2);
            CreateCondition2.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.LESS_THAN((IComparable)variableMap["callflow$.rloop"].Value,(IComparable)3)); });
            CreateCondition2.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch5", callflow, myCall, logHeader));
            IncrementVariableComponent IncrementVariable1 = new IncrementVariableComponent("IncrementVariable1", callflow, myCall, logHeader);
            IncrementVariable1.VariableName = "callflow$.rloop";
            CreateCondition2.ContainerList[0].ComponentList.Add(IncrementVariable1);
            CreateCondition2.ConditionList.Add(() => { return Convert.ToBoolean(true); });
            CreateCondition2.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch6", callflow, myCall, logHeader));
            ExitComponent exitCallflowComponent1 = new ExitComponent("exitCallflowComponent1", callflow, myCall, logHeader);
            CreateCondition2.ContainerList[1].ComponentList.Add(exitCallflowComponent1);
            getrecordid_Conditional.ConditionList.Add(() => { return getrecordid.Result == UserInputComponent.UserInputResults.InvalidDigits || getrecordid.Result == UserInputComponent.UserInputResults.Timeout; });
            getrecordid_Conditional.ContainerList.Add(new SequenceContainerComponent("getrecordid_Conditional_InvalidInput", callflow, myCall, logHeader));
            ExitComponent ExitCallflow5 = new ExitComponent("ExitCallflow5", callflow, myCall, logHeader);
            getrecordid_Conditional.ContainerList[1].ComponentList.Add(ExitCallflow5);
            ConditionalComponent checkGoEvaluationrNot = new ConditionalComponent("checkGoEvaluationrNot", callflow, myCall, logHeader);
            mainFlowComponentList.Add(checkGoEvaluationrNot);
            checkGoEvaluationrNot.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["callflow$.rloop"].Value,4)); });
            checkGoEvaluationrNot.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch2", callflow, myCall, logHeader));
            UserInputComponent RateService = new UserInputComponent("RateService", callflow, myCall, logHeader);
            RateService.AllowDtmfInput = true;
            RateService.MaxRetryCount = 2;
            RateService.FirstDigitTimeout = 10000;
            RateService.InterDigitTimeout = 5000;
            RateService.FinalDigitTimeout = 6000;
            RateService.MinDigits = 1;
            RateService.MaxDigits = 1;
            RateService.ValidDigitList.AddRange(new char[] { '1', '2' });
            RateService.StopDigitList.AddRange(new char[] { '#' });
            RateService.InitialPrompts.Add(new AudioFilePrompt(() => { return "converted_rateing.wav"; }));
            RateService.SubsequentPrompts.Add(new AudioFilePrompt(() => { return "converted_rateing.wav"; }));
            RateService.InvalidDigitPrompts.Add(new AudioFilePrompt(() => { return "Number Typed is invalid.wav"; }));
            RateService.TimeoutPrompts.Add(new AudioFilePrompt(() => { return "converted_retype_number.wav"; }));
            checkGoEvaluationrNot.ContainerList[0].ComponentList.Add(RateService);
            ConditionalComponent RateService_Conditional = new ConditionalComponent("RateService_Conditional", callflow, myCall, logHeader);
            checkGoEvaluationrNot.ContainerList[0].ComponentList.Add(RateService_Conditional);
            RateService_Conditional.ConditionList.Add(() => { return RateService.Result == UserInputComponent.UserInputResults.ValidDigits; });
            RateService_Conditional.ContainerList.Add(new SequenceContainerComponent("RateService_Conditional_ValidInput", callflow, myCall, logHeader));
            PromptPlaybackComponent PromptPlayback2 = new PromptPlaybackComponent("PromptPlayback2", callflow, myCall, logHeader);
            PromptPlayback2.AllowDtmfInput = true;
            PromptPlayback2.Prompts.Add(new AudioFilePrompt(() => { return "converted_thanks_rating.wav"; }));
            RateService_Conditional.ContainerList[0].ComponentList.Add(PromptPlayback2);
            VariableAssignmentComponent mobileNo = new VariableAssignmentComponent("mobileNo", callflow, myCall, logHeader);
            mobileNo.VariableName = "callflow$.mobileNo";
            mobileNo.VariableValueHandler = () => { return variableMap["session.ani"].Value; };
            RateService_Conditional.ContainerList[0].ComponentList.Add(mobileNo);
            LoggerComponent Logger1 = new LoggerComponent("Logger1", callflow, myCall, logHeader);
            Logger1.Level = LoggerComponent.LogLevels.Info;
            Logger1.TextHandler = () => { return Convert.ToString(RateService.Buffer); };
            RateService_Conditional.ContainerList[0].ComponentList.Add(Logger1);
            WebInteractionComponent Evaluation = new WebInteractionComponent("Evaluation", callflow, myCall, logHeader);
            Evaluation.HttpMethod = System.Net.Http.HttpMethod.Post;
            Evaluation.ContentType = "application/json";
            Evaluation.Timeout = 30000;
            Evaluation.UriHandler = () => { return Convert.ToString("https://tabuk.ecolor.com.sa/mock_server.php/api/Evaluation"); };
            Evaluation.ContentHandler = () => { return Convert.ToString(CFDFunctions.CONCATENATE(Convert.ToString("{\"NationalNo\":"),Convert.ToString(variableMap["callflow$.nid"].Value),Convert.ToString(",\"RecordNo\":"),Convert.ToString(variableMap["callflow$.rid"].Value),Convert.ToString(",\"MobileNo\":"),Convert.ToString(variableMap["session.ani"].Value),Convert.ToString(",\"Status\":\""),Convert.ToString(CFDFunctions.EQUAL(RateService.Buffer,"1")),Convert.ToString("\"}"))); };
            RateService_Conditional.ContainerList[0].ComponentList.Add(Evaluation);
            ConditionalComponent conditionalComponent3 = new ConditionalComponent("conditionalComponent3", callflow, myCall, logHeader);
            RateService_Conditional.ContainerList[0].ComponentList.Add(conditionalComponent3);
            conditionalComponent3.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(Evaluation.ResponseStatusCode,200)); });
            conditionalComponent3.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch18", callflow, myCall, logHeader));
            LoggerComponent loggerComponent2 = new LoggerComponent("loggerComponent2", callflow, myCall, logHeader);
            loggerComponent2.Level = LoggerComponent.LogLevels.Info;
            loggerComponent2.TextHandler = () => { return Convert.ToString(Evaluation.ResponseStatusCode); };
            conditionalComponent3.ContainerList[0].ComponentList.Add(loggerComponent2);
            conditionalComponent3.ConditionList.Add(() => { return Convert.ToBoolean(true); });
            conditionalComponent3.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch19", callflow, myCall, logHeader));
            ExitComponent exitCallflowComponent3 = new ExitComponent("exitCallflowComponent3", callflow, myCall, logHeader);
            conditionalComponent3.ContainerList[1].ComponentList.Add(exitCallflowComponent3);
            MenuComponent Menu1 = new MenuComponent("Menu1", callflow, myCall, logHeader);
            Menu1.AllowDtmfInput = true;
            Menu1.MaxRetryCount = 0;
            Menu1.Timeout = 5000;
            Menu1.ValidOptionList.AddRange(new char[] { '9' });
            Menu1.InitialPrompts.Add(new AudioFilePrompt(() => { return "converted_Press-9-Back-To-IVR(1).wav"; }));
            RateService_Conditional.ContainerList[0].ComponentList.Add(Menu1);
            ConditionalComponent Menu1_Conditional = new ConditionalComponent("Menu1_Conditional", callflow, myCall, logHeader);
            RateService_Conditional.ContainerList[0].ComponentList.Add(Menu1_Conditional);
            Menu1_Conditional.ConditionList.Add(() => { return Menu1.Result == MenuComponent.MenuResults.ValidOption && Menu1.SelectedOption == '9'; });
            Menu1_Conditional.ContainerList.Add(new SequenceContainerComponent("Menu1_Conditional_Option9", callflow, myCall, logHeader));
            TransferComponent backToMainMenu = new TransferComponent("backToMainMenu", callflow, myCall, logHeader);
            backToMainMenu.DestinationHandler = () => { return Convert.ToString(774); };
            backToMainMenu.DelayMilliseconds = 500;
            Menu1_Conditional.ContainerList[0].ComponentList.Add(backToMainMenu);
            Menu1_Conditional.ConditionList.Add(() => { return Menu1.Result == MenuComponent.MenuResults.InvalidOption || Menu1.Result == MenuComponent.MenuResults.Timeout; });
            Menu1_Conditional.ContainerList.Add(new SequenceContainerComponent("Menu1_Conditional_TimeoutOrInvalidOption", callflow, myCall, logHeader));
            ExitComponent ExitCallflow3 = new ExitComponent("ExitCallflow3", callflow, myCall, logHeader);
            Menu1_Conditional.ContainerList[1].ComponentList.Add(ExitCallflow3);
            RateService_Conditional.ConditionList.Add(() => { return RateService.Result == UserInputComponent.UserInputResults.InvalidDigits || RateService.Result == UserInputComponent.UserInputResults.Timeout; });
            RateService_Conditional.ContainerList.Add(new SequenceContainerComponent("RateService_Conditional_InvalidInput", callflow, myCall, logHeader));
            PromptPlaybackComponent PromptPlayback6 = new PromptPlaybackComponent("PromptPlayback6", callflow, myCall, logHeader);
            PromptPlayback6.AllowDtmfInput = true;
            PromptPlayback6.Prompts.Add(new AudioFilePrompt(() => { return "converted_thanks_rating.wav"; }));
            RateService_Conditional.ContainerList[1].ComponentList.Add(PromptPlayback6);
            MenuComponent menuComponent1 = new MenuComponent("menuComponent1", callflow, myCall, logHeader);
            menuComponent1.AllowDtmfInput = true;
            menuComponent1.MaxRetryCount = 0;
            menuComponent1.Timeout = 5000;
            menuComponent1.ValidOptionList.AddRange(new char[] { '9' });
            menuComponent1.InitialPrompts.Add(new AudioFilePrompt(() => { return "converted_Press-9-Back-To-IVR(1).wav"; }));
            RateService_Conditional.ContainerList[1].ComponentList.Add(menuComponent1);
            ConditionalComponent menuComponent1_Conditional = new ConditionalComponent("menuComponent1_Conditional", callflow, myCall, logHeader);
            RateService_Conditional.ContainerList[1].ComponentList.Add(menuComponent1_Conditional);
            menuComponent1_Conditional.ConditionList.Add(() => { return menuComponent1.Result == MenuComponent.MenuResults.ValidOption && menuComponent1.SelectedOption == '9'; });
            menuComponent1_Conditional.ContainerList.Add(new SequenceContainerComponent("menuComponent1_Conditional_Option9", callflow, myCall, logHeader));
            TransferComponent transferComponent1 = new TransferComponent("transferComponent1", callflow, myCall, logHeader);
            transferComponent1.DestinationHandler = () => { return Convert.ToString(774); };
            transferComponent1.DelayMilliseconds = 500;
            menuComponent1_Conditional.ContainerList[0].ComponentList.Add(transferComponent1);
            menuComponent1_Conditional.ConditionList.Add(() => { return menuComponent1.Result == MenuComponent.MenuResults.InvalidOption || menuComponent1.Result == MenuComponent.MenuResults.Timeout; });
            menuComponent1_Conditional.ContainerList.Add(new SequenceContainerComponent("menuComponent1_Conditional_TimeoutOrInvalidOption", callflow, myCall, logHeader));
            ExitComponent exitCallflowComponent4 = new ExitComponent("exitCallflowComponent4", callflow, myCall, logHeader);
            menuComponent1_Conditional.ContainerList[1].ComponentList.Add(exitCallflowComponent4);
            checkGoEvaluationrNot.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.LESS_THAN_OR_EQUAL((IComparable)variableMap["callflow$.oloop"].Value,(IComparable)4)); });
            checkGoEvaluationrNot.ContainerList.Add(new SequenceContainerComponent("conditionalComponentBranch4", callflow, myCall, logHeader));
            ExitComponent ExitCallflow2 = new ExitComponent("ExitCallflow2", callflow, myCall, logHeader);
            checkGoEvaluationrNot.ContainerList[1].ComponentList.Add(ExitCallflow2);
            ExitComponent ExitCallflow1 = new ExitComponent("ExitCallflow1", callflow, myCall, logHeader);
            mainFlowComponentList.Add(ExitCallflow1);
            }
            {
            }
            {
            }
            

            // Add a final DisconnectCall component to the main and error handler flows, in order to complete pending prompt playbacks...
            DisconnectCallComponent mainAutoAddedFinalDisconnectCall = new DisconnectCallComponent("mainAutoAddedFinalDisconnectCall", callflow, myCall, logHeader);
            DisconnectCallComponent errorHandlerAutoAddedFinalDisconnectCall = new DisconnectCallComponent("errorHandlerAutoAddedFinalDisconnectCall", callflow, myCall, logHeader);
            mainFlowComponentList.Add(mainAutoAddedFinalDisconnectCall);
            errorFlowComponentList.Add(errorHandlerAutoAddedFinalDisconnectCall);
        }

        private bool IsServerInHoliday(ICall myCall)
        {
            Tenant tenant = myCall.PS.GetTenant();
            return tenant != null && tenant.IsHoliday(new DateTimeOffset(DateTime.Now));
        }

        private bool IsServerOfficeHourActive(ICall myCall)
        {
            Tenant tenant = myCall.PS.GetTenant();
            if (tenant == null) return false;

            string overrideOfficeTime = tenant.GetPropertyValue("OVERRIDEOFFICETIME");
            if (!String.IsNullOrEmpty(overrideOfficeTime))
            {
                if (overrideOfficeTime == "1") // Forced to in office hours
                    return true;
                else if (overrideOfficeTime == "2") // Forced to out of office hours
                    return false;
            }

            DateTime nowDt = DateTime.Now;
            if (tenant.IsHoliday(new DateTimeOffset(nowDt))) return false;

            Schedule officeHours = tenant.Hours;
            Nullable<bool> result = officeHours.IsActiveTime(nowDt);
            return result.GetValueOrDefault(false);
        }

        public Main()
        {
            this.executionStarted = false;
            this.executionFinished = false;
            this.disconnectFlowPending = false;

            this.eventBuffer = new BufferBlock<AbsEvent>();

            this.currentComponentIndex = 0;
            this.mainFlowComponentList = new List<AbsComponent>();
            this.disconnectFlowComponentList = new List<AbsComponent>();
            this.errorFlowComponentList = new List<AbsComponent>();
            this.currentFlowComponentList = mainFlowComponentList;

            this.timerManager = new TimerManager();
            this.timerManager.OnTimeout += (state) => eventBuffer.Post(new TimeoutEvent(state));
            this.variableMap = new Dictionary<string, Variable>();

            AbsTextToSpeechEngine textToSpeechEngine = null;
            AbsSpeechToTextEngine speechToTextEngine = null;
            this.onlineServices = new OnlineServices(textToSpeechEngine, speechToTextEngine);
        }

        public override void Start()
        {
            MyCall.SetBackgroundAudio(false, new string[] { });

            string callID = MyCall?.Caller["chid"] ?? "Unknown";
            string logHeader = $"sudigovt - CallID {callID}";
            this.logFormatter = new LogFormatter(MyCall, logHeader, "Callflow");
            this.promptQueue = new PromptQueue(this, MyCall, "sudigovt", logHeader);
            this.tempWavFileManager = new TempWavFileManager(logFormatter);
            this.timerManager.CallStarted();

            InitializeComponents(this, MyCall, logHeader);
            InitializeVariables(callID);
            
            MyCall.OnTerminated += () => eventBuffer.Post(new CallTerminatedEvent());
            MyCall.OnDTMFInput += x => eventBuffer.Post(new DTMFReceivedEvent(x));

            logFormatter.Trace("Start executing main flow...");
            eventBuffer.Post(new StartEvent());
            Task.Run(() => EventProcessingLoop());

            
        }

        public void PostStartEvent()
        {
            eventBuffer.Post(new StartEvent());
        }

        public void PostDTMFReceivedEvent(char digit)
        {
            eventBuffer.Post(new DTMFReceivedEvent(digit));
        }

        public void PostPromptPlayedEvent()
        {
            eventBuffer.Post(new PromptPlayedEvent());
        }

        public void PostTransferFailedEvent()
        {
            eventBuffer.Post(new TransferFailedEvent());
        }

        public void PostMakeCallResultEvent(bool result)
        {
            eventBuffer.Post(new MakeCallResultEvent(result));
        }

        public void PostCallTerminatedEvent()
        {
            eventBuffer.Post(new CallTerminatedEvent());
        }

        public void PostTimeoutEvent(object state)
        {
            eventBuffer.Post(new TimeoutEvent(state));
        }

        private async Task EventProcessingLoop()
        {
            executionStarted = true;
            while (!executionFinished)
            {
                AbsEvent evt = await eventBuffer.ReceiveAsync();
                await evt?.ProcessEvent(this);
            }
        }
        
        public async Task ProcessStart()
        {
            try
            {
                EventResults eventResult;
                do
                {
                    AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                    logFormatter.Trace("Start executing component '" + currentComponent.Name + "'");
                    eventResult = await currentComponent.Start(timerManager, variableMap, tempWavFileManager, promptQueue);
                }
                while (CheckEventResult(eventResult) == EventResults.MoveToNextComponent);

                if (eventResult == EventResults.Exit) executionFinished = true;
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessDTMFReceived(char digit)
        {
            try
            {
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnDTMFReceived for component '" + currentComponent.Name + "' - Digit: '" + digit + "'");
                EventResults eventResult = CheckEventResult(await currentComponent.OnDTMFReceived(timerManager, variableMap, tempWavFileManager, promptQueue, digit));
                if (eventResult == EventResults.MoveToNextComponent)
                { 
                    if (disconnectFlowPending)
                        await ExecuteDisconnectFlow();
                    else
                        await ProcessStart();
                }
                else if (eventResult == EventResults.Exit)
                    executionFinished = true;
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessPromptPlayed()
        {
            try
            {
                promptQueue.NotifyPlayFinished();
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnPromptPlayed for component '" + currentComponent.Name + "'");
                EventResults eventResult = CheckEventResult(await currentComponent.OnPromptPlayed(timerManager, variableMap, tempWavFileManager, promptQueue));
                if (eventResult == EventResults.MoveToNextComponent)
                { 
                    if (disconnectFlowPending)
                        await ExecuteDisconnectFlow();
                    else
                        await ProcessStart();
                }
                else if (eventResult == EventResults.Exit)
                    executionFinished = true;
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessTransferFailed()
        {
            try
            {
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnTransferFailed for component '" + currentComponent.Name + "'");
                EventResults eventResult = CheckEventResult(await currentComponent.OnTransferFailed(timerManager, variableMap, tempWavFileManager, promptQueue));
                if (eventResult == EventResults.MoveToNextComponent)
                { 
                    if (disconnectFlowPending)
                        await ExecuteDisconnectFlow();
                    else
                        await ProcessStart();
                }
                else if (eventResult == EventResults.Exit)
                    executionFinished = true;
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessMakeCallResult(bool result)
        {
            try
            {
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnMakeCallResult for component '" + currentComponent.Name + "' - Result: '" + result + "'");
                EventResults eventResult = CheckEventResult(await currentComponent.OnMakeCallResult(timerManager, variableMap, tempWavFileManager, promptQueue, result));
                if (eventResult == EventResults.MoveToNextComponent)
                { 
                    if (disconnectFlowPending)
                        await ExecuteDisconnectFlow();
                    else
                        await ProcessStart();
                }
                else if (eventResult == EventResults.Exit)
                    executionFinished = true;
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }

        public async Task ProcessCallTerminated()
        {
            try
            {
                if (executionStarted)
                {
                    // First notify the call termination to the current component
                    AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                    logFormatter.Trace("OnCallTerminated for component '" + currentComponent.Name + "'");

                    // Don't wrap around CheckEventResult, because the call has been already disconnected, 
                    // and the following action to execute depends on the returned value.
                    EventResults eventResult = await currentComponent.OnCallTerminated(timerManager, variableMap, tempWavFileManager, promptQueue);
                    if (eventResult == EventResults.MoveToNextComponent)
                    {
                        // Next, if the current component has completed its job, execute the disconnect flow
                        await ExecuteDisconnectFlow();
                    }
                    else if (eventResult == EventResults.Wait)
                    {
                        // If the user component needs more events, wait for it to finish, and signal here that we need to execute
                        // the disconnect handler flow of the callflow next...
                        disconnectFlowPending = true;
                    }
                    else if (eventResult == EventResults.Exit)
                        executionFinished = true;
                }
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
            finally
            {
                // Finally, delete temporary files
                tempWavFileManager.DeleteFilesAndFolders();
            }
        }

        public async Task ProcessTimeout(object state)
        {
            try
            {
                AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
                logFormatter.Trace("OnTimeout for component '" + currentComponent.Name + "'");
                EventResults eventResult = CheckEventResult(await currentComponent.OnTimeout(timerManager, variableMap, tempWavFileManager, promptQueue, state));
                if (eventResult == EventResults.MoveToNextComponent)
                { 
                    if (disconnectFlowPending)
                        await ExecuteDisconnectFlow();
                    else
                        await ProcessStart();
                }
                else if (eventResult == EventResults.Exit)
                    executionFinished = true;
            }
            catch (Exception exc)
            {
                logFormatter.Error("Error executing last component: " + exc.ToString());
                await ExecuteErrorFlow();
            }
        }


        
    }
}
