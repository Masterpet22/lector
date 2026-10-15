package app.nebulareader.mobile;

import android.os.Bundle;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        registerPlugin(DirectoryAccessPlugin.class);
        super.onCreate(savedInstanceState);
    }
}
