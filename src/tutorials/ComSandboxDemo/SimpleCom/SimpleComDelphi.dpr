library SimpleComDelphi;

uses
  Windows, Activex, ComServ;

const
  CLSID_SimpleCalculator: TGUID = '{11111111-2222-3333-4444-555555555555}';
  IID_ICalculator: TGUID = '{E1234567-ABCD-1234-EF12-0123456789AB}';

type
  // The interface definition (Order must match C# VTable)
  ICalculator = interface(IUnknown)
    ['{E1234567-ABCD-1234-EF12-0123456789AB}']
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall;
  end;

  // The Implementation Class
  TSimpleCalculator = class(TInterfacedObject, ICalculator)
  public
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall;
  end;

  // The Class Factory
  TSimpleCalculatorFactory = class(TInterfacedObject, IClassFactory)
  public
    function CreateInstance(const unkOuter: IUnknown; const iid: TGUID; out obj): HResult; stdcall;
    function LockServer(fLock: BOOL): HResult; stdcall;
  end;

{ TSimpleCalculator }

function TSimpleCalculator.Add(a, b: Integer): Integer; stdcall;
begin
  Result := a + b;
end;

function TSimpleCalculator.GetInfo: WideString; stdcall;
begin
  Result := 'Running the native Delphi (FPC) COM object';
end;

{ TSimpleCalculatorFactory }

function TSimpleCalculatorFactory.CreateInstance(const unkOuter: IUnknown; const iid: TGUID; out obj): HResult; stdcall;
var
  Calc: TSimpleCalculator;
begin
  if unkOuter <> nil then
  begin
    Result := CLASS_E_NOAGGREGATION;
    Exit;
  end;
  
  Calc := TSimpleCalculator.Create;
  Result := Calc.QueryInterface(iid, obj);
end;

function TSimpleCalculatorFactory.LockServer(fLock: BOOL): HResult; stdcall;
begin
  Result := S_OK;
end;

// --- DLL Exports ---

function DllGetClassObject(const clsid, iid: TGUID; out obj): HResult; stdcall;
var
  Factory: TSimpleCalculatorFactory;
begin
  if IsEqualGUID(clsid, CLSID_SimpleCalculator) then
  begin
    Factory := TSimpleCalculatorFactory.Create;
    Result := Factory.QueryInterface(iid, obj);
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