import "@fontsource/comfortaa/700.css";
import "@fontsource/open-sans/400.css";
import "@fontsource/open-sans/600.css";
import { createPinia } from "pinia";
import { createApp } from "vue";

import router from "@/router";

import App from "./App.vue";
import "./styles/index.css";

const app = createApp(App);
const pinia = createPinia();
app.use(router).use(pinia);
app.mount("#app");
