import "@fontsource/eb-garamond/latin-400.css";
import "@fontsource/eb-garamond/latin-500.css";
import "@fontsource/eb-garamond/latin-700.css";
import "@fontsource/vazirmatn/arabic-300.css";
import "@fontsource/vazirmatn/arabic-400.css";
import "@fontsource/vazirmatn/arabic-500.css";
import "@fontsource/vazirmatn/arabic-600.css";
import "@fontsource/vazirmatn/arabic-700.css";
import { createRoot } from "react-dom/client";
import { App } from "./app/App";
import { AuthenticationProvider } from "./features/auth/AuthContext";
import "./styles.css";

createRoot(document.getElementById("root")!).render(
  <AuthenticationProvider>
    <App />
  </AuthenticationProvider>,
);
