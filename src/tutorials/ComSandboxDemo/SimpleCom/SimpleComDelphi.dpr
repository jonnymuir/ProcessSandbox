library SimpleComDelphi;

{$MODE DELPHI}

uses
  Windows, 
  ComObj, 
  ActiveX, 
  ComServ; // This unit implements DllGetClassObject

const
  CLSID_SimpleCalculator: TGUID = '{11111111-2222-3333-4444-555555555555}';

type
  ICalculator = interface(IUnknown)
    ['{E1234567-ABCD-1234-EF12-0123456789AB}']
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall; 
  end;

  TSimpleCalculator = class(TComObject, ICalculator)
  protected
    function Add(a, b: Integer): Integer; stdcall;
    function GetInfo: WideString; stdcall;
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

// These are the 4 standard COM entry points. 
// ComServ provides the implementation for these.
exports
  DllGetClassObject,
  DllCanUnloadNow,
  DllRegisterServer,
  DllUnregisterServer;

begin
  // Create the factory inside the begin-end block or initialization
  TComObjectFactory.Create(
    ComServer, 
    TSimpleCalculator, 
    CLSID_SimpleCalculator, 
    'SimpleCalculator', 
    'Simple Calculator COM Object',
    ciMultiInstance,
    tmBoth
  );
end.