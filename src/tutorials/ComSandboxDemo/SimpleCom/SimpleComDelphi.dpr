library SimpleComDelphi;

{$MODE DELPHI}

uses
  Windows, ActiveX, ComObj, SysUtils;

type
  // Manually define the record since we aren't using the PsAPI unit
  TProcessMemoryCounters = record
    cb: DWORD;
    PageFaultCount: DWORD;
    PeakWorkingSetSize: SIZE_T;
    WorkingSetSize: SIZE_T;
    QuotaPeakPagedPoolUsage: SIZE_T;
    QuotaPagedPoolUsage: SIZE_T;
    QuotaPeakNonPagedPoolUsage: SIZE_T;
    QuotaNonPagedPoolUsage: SIZE_T;
    PagefileUsage: SIZE_T;
    PeakPagefileUsage: SIZE_T;
  end;

// Manually import the function from psapi.dll
function GetProcessMemoryInfo(Process: HANDLE; ppsmemCounters: Pointer; cb: DWORD): BOOL; stdcall; external 'psapi.dll';
function GetProcessHandleCount(hProcess: HANDLE; var pdwHandleCount: DWORD): BOOL; stdcall; external 'kernel32.dll';

const
  CLSID_SimpleCalculator: TGUID = '{11111111-2222-3333-4444-555555555555}';
  IID_ICalculator: TGUID        = '{E1234567-ABCD-1234-EF12-0123456789AB}';

type
  ICalculator = interface(IUnknown)
    ['{E1234567-ABCD-1234-EF12-0123456789AB}']
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: TBSTR; stdcall;
  end;

  { The Calculator Implementation }
  TSimpleCalculator = class(TInterfacedObject, ICalculator)
  public
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: TBSTR; stdcall;
  end;

  { The Class Factory Implementation }
  TSimpleClassFactory = class(TInterfacedObject, IClassFactory)
  public
    function CreateInstance(const unkOuter: IUnknown; const iid: TGUID; out obj): HResult; stdcall;
    function LockServer(fLock: BOOL): HResult; stdcall;
  end;

{ TSimpleCalculator }

function TSimpleCalculator.Add(a, b: Integer): Integer; stdcall;
begin
  Result := a + b;
end;

function TSimpleCalculator.GetInfo: TBSTR; stdcall;
var
  S: WideString;
  PID: DWORD;
  PMC: TProcessMemoryCounters;
  HandleCount: DWORD;
  WorkingSet: UInt64;
  Connection: OleVariant;
  Recordset: OleVariant;
  DbResult: string;
  ConnStr: string;
begin

  // Example of calling out to com objects
  CoInitialize(nil);
  try
    try
      // Create the ADO Connection Object via Late Binding
      Connection := CreateOleObject('ADODB.Connection');
      
      // efine Connection String (Example: Access, SQL Server, or Excel)
      // For this test, we'll use a local Access provider or DSN
      ConnStr := 'Provider=Microsoft.ACE.OLEDB.12.0;Data Source=test.accdb;';
      
      // Note: If you don't have a DB file, we can simulate a result 
      // or connect to a local SQL Express instance.
      // Connection.Open(ConnStr); 

      // Create Recordset and Run Query
      Recordset := CreateOleObject('ADODB.Recordset');
      
      { 
        Uncommenting the lines below would perform the actual DB hit:
        Recordset.Open('SELECT TOP 1 SomeColumn FROM SomeTable', Connection);
        if not Recordset.EOF then
           DbResult := Recordset.Fields['SomeColumn'].Value;
      }
      
      // For the sake of a "provable" COM call that doesn't crash without a DB file:
      DbResult := 'ADO Version: ' + string(Connection.Version);

    except
      on E: Exception do
        DbResult := 'ADO Error: ' + E.Message;
    end;
  finally
    // 5. Cleanup
    Recordset := Unassigned;
    Connection := Unassigned;
    CoUninitialize;
  end;
  // End example of calling out to com


  PID := GetCurrentProcessID;
  
  WorkingSet := 0;
  FillChar(PMC, SizeOf(PMC), 0);
  PMC.cb := SizeOf(PMC);
  
  // 1. Memory Stats (from our manual psapi import)
  if GetProcessMemoryInfo(GetCurrentProcess, @PMC, SizeOf(PMC)) then
    WorkingSet := PMC.WorkingSetSize;

  // 2. Handle Count (from our manual kernel32 import)
  HandleCount := 0;
  GetProcessHandleCount(GetCurrentProcess, HandleCount);

  S := WideFormat(
    '{' +
    '"engine": "Running the manual Delphi FPC COM object. DBQuery: %s",' +
    '"pid": %d,' +
    '"memoryBytes": %d,' +
    '"handles": %d' +
    '}',
    [DbResult, PID, WorkingSet, HandleCount]
  );

  Result := SysAllocString(PWideChar(S));
end;

{ TSimpleClassFactory }

function TSimpleClassFactory.CreateInstance(const unkOuter: IUnknown; const iid: TGUID; out obj): HResult; stdcall;
var
  CalcIntf: ICalculator;
begin
  Pointer(obj) := nil;
  if unkOuter <> nil then Exit(CLASS_E_NOAGGREGATION);

  try
    // 1. Create the object and immediately assign to an interface variable
    // This sets RefCount to 1.
    CalcIntf := TSimpleCalculator.Create;
    
    // 2. Query for the requested IID (usually ICalculator or IUnknown)
    Result := CalcIntf.QueryInterface(iid, obj);
  except
    Result := E_UNEXPECTED;
  end;
end;

function TSimpleClassFactory.LockServer(fLock: BOOL): HResult; stdcall;
begin
  Result := S_OK;
end;

{ DLL Exports }

function DllGetClassObject(const clsid, iid: TGUID; out obj): HResult; stdcall;
var
  FactoryIntf: IClassFactory;
begin
  Pointer(obj) := nil;
  if IsEqualGUID(clsid, CLSID_SimpleCalculator) then
  begin
    FactoryIntf := TSimpleClassFactory.Create;
    Result := FactoryIntf.QueryInterface(iid, obj);
  end
  else
    Result := CLASS_E_CLASSNOTAVAILABLE;
end;

function DllCanUnloadNow: HResult; stdcall;
begin
  Result := S_FALSE;
end;

exports
  DllGetClassObject,
  DllCanUnloadNow;

begin
end.