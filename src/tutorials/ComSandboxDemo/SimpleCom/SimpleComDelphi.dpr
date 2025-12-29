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
  Connection, Recordset: OleVariant;
  Response: string;
  ConnStr: string;
begin

  // Example of calling out to com objects
  CoInitialize(nil);
  try
    try
      Connection := CreateOleObject('ADODB.Connection');
      
      // Connection string for Entra Managed Identity
      ConnStr := 'Provider=MSOLEDBSQL;' +
                 'Data Source=com-sandbox.database.windows.net;' +
                 'Initial Catalog=free-sql-db-3575767;' +
                 'Authentication=ActiveDirectoryMSI;' +
                 'Encrypt=yes;' +
                 'TrustServerCertificate=no;';

      Connection.ConnectionString := ConnStr;
      Connection.ConnectionTimeout := 30;
      Connection.Open;

      // Run a query to prove who we are connected as
      Recordset := CreateOleObject('ADODB.Recordset');
      Recordset.Open('SELECT SUSER_SNAME() as CurrentUser, DB_NAME() as CurrentDB', Connection);
      
      if not Recordset.EOF then
      begin
        Response := Format(
          '{"status": "success", "user": "%s", "database": "%s", "engine": "FPC/Lazarus"}',
          [string(Recordset.Fields['CurrentUser'].Value), 
           string(Recordset.Fields['CurrentDB'].Value)]
        );
      end;

    except
      on E: Exception do
        Response := Format('{"status": "error", "message": "%s"}', [E.Message]);
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
    [Response, PID, WorkingSet, HandleCount]
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